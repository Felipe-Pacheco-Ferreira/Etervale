using System.Net.Sockets;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Services;

// Online: the game server accepted a connection at the last check. Since: when that state began, as far as the site saw.
public sealed record ServerStatus(bool Online, DateTimeOffset CheckedAt, DateTimeOffset Since);

// Checks every GameServer:ProbeSeconds that the game server accepts TCP connections; pages read the last result and never
// wait for a check. The probe closes the socket before any TLS handshake, so it costs the server one accepted socket.
public sealed class ServerStatusMonitor(WebSettings settings, TimeProvider time, ILogger<ServerStatusMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private ServerStatus? _current;

    // Null until the first check finished.
    public ServerStatus? Current => Volatile.Read(ref _current);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.GameServer.ProbeSeconds), time);
        do
        {
            var online = await ProbeAsync(stoppingToken);
            var now = time.GetUtcNow();
            var previous = Current;
            if (previous?.Online != online)
            {
                logger.LogInformation("Game server {Host}:{Port} is {State}", settings.GameServer.Host, settings.GameServer.Port,
                    online ? "online" : "offline");
            }

            Volatile.Write(ref _current, new ServerStatus(online, now, previous is not null && previous.Online == online ? previous.Since : now));
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> ProbeAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(settings.GameServer.Host, settings.GameServer.Port, timeout.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
