using PristonTale.Application.Chat;
using PristonTale.DiscordBot.Sources;

namespace PristonTale.DiscordBot;

// The bot's loop: the chat relays and tickets every PollSeconds, the worlds and events every StatusSeconds and the news
// every NewsSeconds. A step that fails is logged and tried again on its next turn.
public sealed partial class DiscordBotService
{
    private const string PublicStream = "public";
    private const string PrivateStream = "private";
    private readonly WorldWatch _worlds = new();
    private readonly EventWatch _events = new();
    private readonly Dictionary<string, ActivityCursor> _cursors = [];
    private string _presence = "";

    private async Task WatchAsync(CancellationToken ct)
    {
        var poll = TimeSpan.FromSeconds(settings.Watch.PollSeconds);
        var nextStatus = DateTimeOffset.MinValue;
        var nextNews = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(poll, time);
        do
        {
            await StepAsync("public chat", () => RelayActivityAsync(PublicStream, settings.Activity.PublicPath, settings.Discord.Channels.PublicChat, ct));
            await StepAsync("private chat", () => RelayActivityAsync(PrivateStream, settings.Activity.PrivatePath, settings.Discord.Channels.PrivateChat, ct));
            await StepAsync("tickets", () => SyncTicketsAsync(ct));
            var now = time.GetUtcNow();
            if (now >= nextStatus)
            {
                nextStatus = now.AddSeconds(settings.Watch.StatusSeconds);
                await StepAsync("worlds", () => WatchWorldsAsync(ct));
                await StepAsync("events", () => WatchEventsAsync(ct));
            }

            if (now >= nextNews)
            {
                nextNews = now.AddSeconds(settings.Watch.NewsSeconds);
                await StepAsync("news", () => WatchNewsAsync(ct));
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task StepAsync(string name, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Discord bot step {Step} failed", name);
        }
    }

    private async Task RelayActivityAsync(string stream, string path, ulong channel, CancellationToken ct)
    {
        if (path.Length == 0 || channel == 0 || await ActivityCursor.ReadAsync(BotSettings.BesideExecutable(path), ct) is not { } snapshot)
        {
            return;
        }

        if (!_cursors.TryGetValue(stream, out var cursor))
        {
            cursor = new ActivityCursor(_state.Activity.GetValueOrDefault(stream));
            _cursors[stream] = cursor;
        }

        var (fresh, position) = cursor.Peek(snapshot);
        var lines = fresh
            .Where(static message => !(message.Channel == "trade" && ExternalChat.IsFrom(ExternalChat.DiscordSource, message.Text)))
            .Select(BotTexts.ChatLine)
            .ToArray();
        await SendAsync(channel, lines);
        cursor.Commit(position);
        if (position != _state.Activity.GetValueOrDefault(stream))
        {
            await SaveStateAsync(state => state.Activity[stream] = position, ct);
        }
    }

    private async Task WatchWorldsAsync(CancellationToken ct)
    {
        var worlds = await site.WorldsAsync(ct);
        await SendAsync(settings.Discord.Channels.Announcements, _worlds.Changes(worlds).Select(BotTexts.WorldChanged));
        var presence = BotTexts.PlayersOnline(worlds);
        if (presence != _presence)
        {
            _presence = presence;
            await _client.SetCustomStatusAsync(presence);
        }
    }

    private async Task WatchEventsAsync(CancellationToken ct)
    {
        var calendar = await data.CalendarAsync(ct);
        var started = _events.Started(calendar);
        await SendAsync(settings.Discord.Channels.Announcements,
            started.Select(occurrence => BotTexts.EventStarted(occurrence, calendar.Calendar.BossesOf(occurrence), calendar.Zone)));
    }

    private async Task WatchNewsAsync(CancellationToken ct)
    {
        var items = await site.NewsAsync(ct);
        var firstRead = _state.News is null;
        var fresh = NewsWatch.Fresh(_state, items);
        await SendAsync(settings.Discord.Channels.Announcements, fresh.Select(BotTexts.NewsPost));
        if (firstRead || fresh.Count > 0)
        {
            await SaveStateAsync(state => NewsWatch.Mark(state, firstRead ? items : fresh), ct);
        }
    }

    private async Task SaveStateAsync(Action<BotState> change, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            change(_state);
            await _state.SaveAsync(_statePath, ct);
        }
        finally
        {
            _stateGate.Release();
        }
    }
}
