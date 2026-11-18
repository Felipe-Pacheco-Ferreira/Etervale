using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Contracts.Social;

namespace PristonTale.Server.Net;

// The bot link: a Kestrel listener of its own on loopback (the host stays a generic host) that takes the Discord bot's
// trade-channel lines and remote game-master commands. Every request carries the shared key; anything else answers 401.
internal sealed class BotLinkService(IConfiguration configuration, ChatDirectory chat, GameMasterCommands gameMasters, ILogger<BotLinkService> logger)
    : IHostedService, IAsyncDisposable
{
    private const int MostBodyBytes = 4096;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private readonly BotLinkSettings _settings = BotLinkSettings.From(configuration);
    private readonly ExternalChatFlood _flood = new();
    private WebApplication? _app;

    // The address the link listens at once started; null while it is off.
    public Uri? Address { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var url = new Uri(_settings.Url);
        // An empty builder reads no configuration source; every setting comes from BotLink.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = MostBodyBytes;
            GameTcpServer.Listen(options, url, static _ => { });
        });
        var app = builder.Build();
        app.Run(HandleAsync);
        await app.StartAsync(cancellationToken);
        _app = app;
        Address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        logger.LogInformation("Bot link listening at {Address}", Address);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is { } app)
        {
            _app = null;
            using var timeout = new CancellationTokenSource(StopTimeout);
            await app.StopAsync(timeout.Token);
            await app.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Authorized(context.Request.Headers.Authorization.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var path = context.Request.Path;
        if (path.Equals(BotLinkPaths.Chat, StringComparison.Ordinal))
        {
            var request = await ReadAsync<BotChatRequest>(context);
            var outcome = request is null
                ? ExternalChatOutcome.Empty
                : ExternalChat.Relay(chat, _flood, Environment.TickCount64, ExternalChat.DiscordSource, request.Author ?? "", request.Text ?? "");
            context.Response.StatusCode = outcome switch
            {
                ExternalChatOutcome.Sent => StatusCodes.Status204NoContent,
                ExternalChatOutcome.Flooded => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status400BadRequest
            };
            return;
        }

        if (path.Equals(BotLinkPaths.Command, StringComparison.Ordinal))
        {
            var request = await ReadAsync<BotCommandRequest>(context);
            if (request is null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrEmpty(request.Line))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var issuer = new RemoteGameMaster(request.UserId, request.Name, Math.Clamp(request.Level, GameMasterLevel.Player, _settings.MaxGmLevel));
            var result = await gameMasters.HandleRemoteAsync(issuer, request.Line);
            await context.Response.WriteAsJsonAsync(new BotCommandResponse(result.Outcome.ToString(), result.Replies));
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private bool Authorized(string header)
    {
        const string Scheme = "Bearer ";
        if (!header.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[Scheme.Length..]), Encoding.UTF8.GetBytes(_settings.Key));
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        try
        {
            return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return null;
        }
    }
}
