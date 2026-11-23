using System.Globalization;
using System.Threading.Channels;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Discord;

// Discord Rich Presence through the Discord client's local IPC. Update runs on the game thread and only hands a
// changed activity to a worker task; the worker connects, sends and retries. Without a running Discord, or when
// Discord refuses the application, nothing is shown and the game is not disturbed.
public sealed class DiscordPresence : IDisposable
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly IDiscordIpcConnector _connector;
    private readonly IClientLog _log;
    private readonly DiscordPresenceTracker _tracker = new();
    private readonly Channel<DiscordPresenceRequest> _requests = Channel.CreateBounded<DiscordPresenceRequest>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private Stream _session;
    private string _sessionApplication;
    private long _nonce;
    private string _lastError;

    public DiscordPresence(IDiscordIpcConnector connector, IClientLog log)
    {
        _connector = connector;
        _log = log;
        _worker = Task.Run(() => RunAsync(_stop.Token));
    }

    // Null where this platform has no local Discord.
    public static DiscordPresence ForThisPlatform(IClientLog log)
    {
        var connector = DiscordIpcConnectors.ForThisPlatform();
        return connector is null ? null : new DiscordPresence(connector, log);
    }

    public void Update(ClientSettings settings, PlayingStatus status)
    {
        if (_tracker.TryChange(settings.DiscordPresence, settings.DiscordApplicationId, settings.Language, status,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var request))
        {
            _requests.Writer.TryWrite(request);
        }
    }

    private async Task RunAsync(CancellationToken stop)
    {
        var reader = _requests.Reader;
        DiscordPresenceRequest pending = null;
        try
        {
            while (true)
            {
                if (pending is null)
                {
                    pending = await reader.ReadAsync(stop).ConfigureAwait(false);
                }
                else
                {
                    await WaitForRetryAsync(reader, stop).ConfigureAwait(false);
                }

                while (reader.TryRead(out var newer))
                {
                    pending = newer;
                }

                if (await TryApplyAsync(pending, stop).ConfigureAwait(false))
                {
                    pending = null;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
        finally
        {
            CloseSession();
        }
    }

    private static async Task WaitForRetryAsync(ChannelReader<DiscordPresenceRequest> reader, CancellationToken stop)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
        wait.CancelAfter(RetryDelay);
        try
        {
            await reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
        }
    }

    // False when Discord could not be reached; the request is retried after RetryDelay or when a newer one arrives.
    private async Task<bool> TryApplyAsync(DiscordPresenceRequest request, CancellationToken stop)
    {
        if (request.Activity is null)
        {
            CloseSession();
            return true;
        }

        if (_session is not null && !string.Equals(_sessionApplication, request.ApplicationId, StringComparison.Ordinal))
        {
            CloseSession();
        }

        // A session Discord closed meanwhile fails on the first write; one new connection is tried at once.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (_session is null && !await ConnectAsync(request.ApplicationId, stop).ConfigureAwait(false))
                {
                    return false;
                }

                await SendActivityAsync(request.Activity, stop).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException
                or ObjectDisposedException or OperationCanceledException)
            {
                CloseSession();
                stop.ThrowIfCancellationRequested();
            }
        }

        return false;
    }

    private async Task<bool> ConnectAsync(string applicationId, CancellationToken stop)
    {
        var stream = await _connector.TryConnectAsync(stop).ConfigureAwait(false);
        if (stream is null)
        {
            return false;
        }

        _session = stream;
        _sessionApplication = applicationId;
        await DiscordIpcFrame.WriteAsync(stream, DiscordIpcOpcode.Handshake, DiscordIpcMessages.Handshake(applicationId), stop).ConfigureAwait(false);
        var reply = await ReadReplyAsync(stop).ConfigureAwait(false);
        if (reply.Event != DiscordIpcMessages.ReadyEvent)
        {
            Report($"Discord refused the application {applicationId}: {reply.Error ?? "no reason given"}.");
            CloseSession();
            return false;
        }

        _log.Info("[Discord] Connected; the activity is shown on the player's profile.");
        _lastError = null;
        return true;
    }

    private async Task SendActivityAsync(DiscordActivity activity, CancellationToken stop)
    {
        var nonce = (++_nonce).ToString(CultureInfo.InvariantCulture);
        var payload = DiscordIpcMessages.SetActivity(Environment.ProcessId, activity, nonce);
        await DiscordIpcFrame.WriteAsync(_session, DiscordIpcOpcode.Frame, payload, stop).ConfigureAwait(false);
        while (true)
        {
            var reply = await ReadReplyAsync(stop).ConfigureAwait(false);
            if (reply.Nonce == nonce)
            {
                if (reply.Event == DiscordIpcMessages.ErrorEvent)
                {
                    Report($"Discord refused the activity: {reply.Error ?? "no reason given"}.");
                }

                return;
            }
        }
    }

    // The next command reply; pings are answered and a close frame ends the session.
    private async Task<DiscordIpcReply> ReadReplyAsync(CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(ReplyTimeout);
        while (true)
        {
            var message = await DiscordIpcFrame.ReadAsync(_session, timeout.Token).ConfigureAwait(false);
            switch (message.Opcode)
            {
                case DiscordIpcOpcode.Ping:
                    await DiscordIpcFrame.WriteAsync(_session, DiscordIpcOpcode.Pong, message.Payload, timeout.Token).ConfigureAwait(false);
                    break;
                case DiscordIpcOpcode.Close:
                    var closed = DiscordIpcMessages.ReadReply(message.Payload);
                    return closed with { Event = null };
                case DiscordIpcOpcode.Frame:
                    return DiscordIpcMessages.ReadReply(message.Payload);
            }
        }
    }

    private void Report(string error)
    {
        if (error != _lastError)
        {
            _lastError = error;
            _log.Warn("[Discord] " + error);
        }
    }

    private void CloseSession()
    {
        _session?.Dispose();
        _session = null;
        _sessionApplication = null;
    }

    // Closing the connection makes Discord clear the activity.
    public void Dispose()
    {
        _requests.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            _worker.Wait(StopTimeout);
        }
        catch (AggregateException exception)
        {
            _log.Error("[Discord] The presence worker failed", exception);
        }

        _stop.Dispose();
    }
}
