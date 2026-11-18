using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.WebSockets;
using PristonTale.Protocol.Transport;

namespace PristonTale.Server.Net;

// The WebSocket edge for browser clients, which cannot open raw TCP: a Kestrel listener of its own (the host stays a
// generic host) that upgrades requests on one path and runs the same ClientSession over WebSocketTransport. Its
// connections count against the same limits as the TLS listener's, keyed by the player's address after trusted proxies.
public sealed partial class GameTcpServer
{
    private readonly WebSocketEdgeSettings _webSocket = WebSocketEdgeSettings.From(configuration);
    private readonly TaskCompletionSource<Uri?> _webSocketAddress = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LogSampler _originLog = new(5000);

    // The ws:// or wss:// address the edge serves once it listens; null when Network:WebSocket:Enabled is off.
    public Task<Uri?> WebSocketAddress => _webSocketAddress.Task;

    // How long stopping waits for the edge's requests after every session was closed.
    private static readonly TimeSpan EdgeStopTimeout = TimeSpan.FromSeconds(5);
    private X509Certificate2? _webSocketCertificate;

    private async Task<WebApplication?> StartWebSocketEdgeAsync(SessionServices services, CancellationToken stoppingToken)
    {
        if (!_webSocket.Enabled)
        {
            _webSocketAddress.TrySetResult(null);
            return null;
        }

        WebApplication? app = null;
        try
        {
            var url = new Uri(_webSocket.Url);
            var secure = url.Scheme == Uri.UriSchemeHttps;
            _webSocketCertificate = secure && _webSocket.ResolvedCertificatePath is { } path ? ServerCertificate.LoadFile(path, _webSocket.CertificatePassword) : null;
            var certificate = _webSocketCertificate;
            // An empty builder reads no configuration source; every setting comes from Network:WebSocket.
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.WebHost.UseKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMilliseconds(_limits.HandshakeTimeoutMs);
                options.Limits.MaxConcurrentUpgradedConnections = _limits.MaxConnections > 0 ? _limits.MaxConnections : null;
                void Configure(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen)
                {
                    if (certificate is not null)
                    {
                        listen.UseHttps(certificate);
                    }
                    else if (secure)
                    {
                        listen.UseHttps();
                    }
                }

                Listen(options, url, Configure);
                if (_webSocket.LanAddress.Length > 0)
                {
                    options.Listen(IPAddress.Parse(_webSocket.LanAddress), url.Port, Configure);
                }
            });
            builder.Services.AddWebSockets(static _ => { });
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                options.ForwardLimit = 1;
                foreach (var proxy in _webSocket.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });
            app = builder.Build();
            app.UseForwardedHeaders();
            app.UseWebSockets();
            app.Run(context => AcceptWebSocketAsync(context, services, stoppingToken));
            await app.StartAsync(stoppingToken);
            var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
            var address = new UriBuilder(bound)
            {
                Scheme = secure ? Uri.UriSchemeWss : Uri.UriSchemeWs,
                Host = url.Host,
                Path = _webSocket.Path
            }.Uri;
            logger.LogInformation("Etervale WebSocket edge listening at {Address}", address);
            if (_webSocket.AllowedOrigins.Length == 0)
            {
                logger.LogWarning("Network:WebSocket:AllowedOrigins is empty: every browser page is refused");
            }

            _webSocketAddress.TrySetResult(address);
            return app;
        }
        catch (Exception ex)
        {
            _webSocketAddress.TrySetException(ex);
            await StopWebSocketEdgeAsync(app);
            throw;
        }
    }

    internal static void Listen(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options, Uri url,
        Action<Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions> configure)
    {
        if (url.IsLoopback && !IPAddress.TryParse(url.Host, out _))
        {
            options.ListenLocalhost(url.Port, configure);
        }
        else if (IPAddress.TryParse(url.Host.Trim('[', ']'), out var ip))
        {
            options.Listen(ip, url.Port, configure);
        }
        else
        {
            options.ListenAnyIP(url.Port, configure);
        }
    }

    // Upgrades stop at the stopping token (AcceptWebSocketAsync), so the requests still open are the closed sessions'.
    private async Task StopWebSocketEdgeAsync(WebApplication? edge)
    {
        if (edge is not null)
        {
            using var timeout = new CancellationTokenSource(EdgeStopTimeout);
            await edge.StopAsync(timeout.Token);
            await edge.DisposeAsync();
        }

        _webSocketCertificate?.Dispose();
        _webSocketCertificate = null;
    }

    private async Task AcceptWebSocketAsync(HttpContext context, SessionServices services, CancellationToken stoppingToken)
    {
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals(StatusPath, StringComparison.OrdinalIgnoreCase))
        {
            await WriteStatusAsync(context);
            return;
        }

        if (!context.Request.Path.Equals(_webSocket.Path, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }

        string? origin = context.Request.Headers.Origin;
        if (!_webSocket.AllowsOrigin(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            if (_originLog.TryEnter(Environment.TickCount64, out var suppressed))
            {
                logger.LogWarning("Refused a WebSocket from origin {Origin} ({Suppressed} similar refusals not logged)", origin, suppressed);
            }

            return;
        }

        if (stoppingToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var remote = context.Connection.RemoteIpAddress is { } ip ? new IPEndPoint(ip, context.Connection.RemotePort) : null;
        var address = NetworkLimits.AddressOf(remote);
        if (Refusal(address) is var refusal and not ConnectionRefusal.None)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            LogRefused(address, refusal);
            return;
        }

        Interlocked.Increment(ref _open);
        Interlocked.Increment(ref _handshakes);
        var connection = RunWebSocketClientAsync(context, services, remote, address, stoppingToken);
        _connections[connection] = 0;
        try
        {
            await connection;
        }
        finally
        {
            _connections.TryRemove(connection, out _);
        }
    }

    // The world's load for the site's world list: players in the world and the connection cap (0 when uncapped). Only
    // pages of the allowed origins may read it from a script; the page also times the request as the world's ping.
    private const string StatusPath = "/status";

    private Task WriteStatusAsync(HttpContext context)
    {
        string? origin = context.Request.Headers.Origin;
        if (origin is not null && _webSocket.AllowsOrigin(origin))
        {
            context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Vary = "Origin";
        }

        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new WorldLoad(world.PlayerCount, Math.Max(0, _limits.MaxConnections)));
    }

    private sealed record WorldLoad(int Players, int Capacity);

    private async Task RunWebSocketClientAsync(HttpContext context, SessionServices services, IPEndPoint? remote, IPAddress? address,
        CancellationToken cancellationToken)
    {
        try
        {
            FrameConnection connection;
            try
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync();
                var local = context.Connection.LocalIpAddress is { } ip ? new IPEndPoint(ip, context.Connection.LocalPort) : null;
                connection = WebSocketTransport.Accept(socket, remote, local);
            }
            catch (Exception ex)
            {
                _perAddress?.Release(address);
                if (!cancellationToken.IsCancellationRequested && _closedLog.TryEnter(Environment.TickCount64, out var skipped))
                {
                    logger.LogWarning("WebSocket upgrade from {Address} failed: {Message} ({Suppressed} similar closes not logged)", address, ex.Message, skipped);
                }

                return;
            }
            finally
            {
                Interlocked.Decrement(ref _handshakes);
                _handshakesPerAddress?.Release(address);
            }

            await RunSessionAsync(connection, services, address, cancellationToken, browserTickets: true);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
