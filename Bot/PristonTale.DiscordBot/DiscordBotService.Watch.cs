using Discord;
using PristonTale.Application.Chat;
using PristonTale.Contracts.Social;
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
            foreach (var server in settings.GameServers)
            {
                var name = server.Name.Trim();
                await StepAsync("public chat " + name, () => RelayActivityAsync(StreamKey(PublicStream, name), server.Activity.PublicPath, name, PublicChannel, ct));
                await StepAsync("private chat " + name, () => RelayActivityAsync(StreamKey(PrivateStream, name), server.Activity.PrivatePath, name, settings.Discord.Channels.PrivateChannel, ct));
            }

            await StepAsync("tickets", () => SyncTicketsAsync(ct));
            await StepAsync("gm audit", () => WatchGmAuditAsync(ct));
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
                await StepAsync("development notes", () => WatchDevNotesAsync(ct));
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

    // The state's key of a server's relay: the plain stream for the only, unnamed server, so its position survives.
    public static string StreamKey(string stream, string server) => server.Length == 0 ? stream : stream + ":" + server;

    // The global chat gets the spoken lines and notices, the trade channel its own lines.
    private ulong PublicChannel(string channel) =>
        channel == "trade" ? settings.Discord.Channels.TradeChat : settings.Discord.Channels.PublicChat;

    private async Task RelayActivityAsync(string stream, string path, string server, Func<string, ulong> target, CancellationToken ct)
    {
        if (path.Length == 0 || await ActivityCursor.ReadAsync(BotSettings.BesideExecutable(path), ct) is not { } snapshot)
        {
            return;
        }

        if (!_cursors.TryGetValue(stream, out var cursor))
        {
            cursor = new ActivityCursor(_state.Activity.GetValueOrDefault(stream));
            _cursors[stream] = cursor;
        }

        var (fresh, position) = cursor.Peek(snapshot);
        // Feats go to their feed as cards, the rest as chat; lines whose channel is 0 are passed over. A failed send leaves
        // the cursor, so every line is tried again.
        foreach (var message in fresh)
        {
            if (message.Feat is { } feat && _client.GetChannel(settings.Discord.Channels.FeatChannel(feat.Kind)) is IMessageChannel feed)
            {
                await SendFeatAsync(feed, FeatCards.For(feat, server, titles, message.Sequence), ping: feat.Kind == GameFeat.Boss);
            }
        }

        var groups = fresh
            .Where(static message => message.Feat is null)
            .Where(static message => !(message.Channel == "trade" && ExternalChat.IsFrom(ExternalChat.DiscordSource, message.Text)))
            .GroupBy(message => target(message.Channel))
            .Where(static group => group.Key != 0);
        foreach (var group in groups)
        {
            if (_client.GetChannel(group.Key) is IMessageChannel channel)
            {
                await SendMessagesAsync(channel, BotTexts.ChatBlocks(group.Select(message => BotTexts.ChatLine(message, server))));
            }
        }

        cursor.Commit(position);
        if (position != _state.Activity.GetValueOrDefault(stream))
        {
            await SaveStateAsync(state => state.Activity[stream] = position, ct);
        }
    }

    // A boss slain pings @everyone (Discord:PingEveryone); a PvP kill does not.
    private async Task SendFeatAsync(IMessageChannel channel, BotCard card, bool ping)
    {
        ping &= settings.Discord.PingEveryone;
        await channel.SendMessageAsync(ping ? BotTexts.Everyone : null, embed: CardEmbed(card), allowedMentions: Mentions(ping));
    }

    private static Embed CardEmbed(BotCard card)
    {
        var embed = new EmbedBuilder()
            .WithTitle(BotTexts.Cut(card.Title, EmbedTitleLimit))
            .WithDescription(card.Description)
            .WithColor(new Color(card.Color))
            .WithFooter(card.Footer)
            .WithCurrentTimestamp();
        foreach (var field in card.Fields)
        {
            embed.AddField(field.Name, field.Value, field.Inline);
        }

        return embed.Build();
    }

    private const int EmbedTitleLimit = 256;

    private async Task WatchWorldsAsync(CancellationToken ct)
    {
        var worlds = await site.WorldsAsync(ct);
        // During maintenance the worlds still are watched, but their going down and up is expected and not announced.
        var changes = _worlds.Changes(worlds).Select(BotTexts.WorldChanged).Where(_ => !_state.Maintenance).ToArray();
        await SendAsync(settings.Discord.Channels.WorldStatusChannel, changes, ping: true);
        await UpdateStatusPanelAsync(BotTexts.StatusPanel(worlds, _state.Maintenance, _state.MaintenanceReason), repost: changes.Length > 0, ct);
        var presence = BotTexts.PlayersOnline(worlds);
        if (presence != _presence)
        {
            _presence = presence;
            await _client.SetCustomStatusAsync(presence);
        }
    }

    // The standing card is edited only when what it shows changed (its time is the last change), and posted again, below
    // the up/down lines, when it was deleted or after a world changed so it stays the channel's last message.
    private async Task UpdateStatusPanelAsync(BotCard panel, bool repost, CancellationToken ct)
    {
        var shown = string.Join("\n", panel.Fields.Select(static field => field.Name + field.Value));
        if (shown == _panelShown && !repost || _client.GetChannel(settings.Discord.Channels.WorldStatusChannel) is not IMessageChannel channel)
        {
            return;
        }

        if (!repost && _state.StatusPanel != 0 && await channel.GetMessageAsync(_state.StatusPanel) is IUserMessage message)
        {
            await message.ModifyAsync(edit => edit.Embed = CardEmbed(panel));
        }
        else
        {
            if (_state.StatusPanel != 0 && await channel.GetMessageAsync(_state.StatusPanel) is IUserMessage old)
            {
                await old.DeleteAsync();
            }

            var posted = await channel.SendMessageAsync(embed: CardEmbed(panel), allowedMentions: AllowedMentions.None);
            await SaveStateAsync(state => state.StatusPanel = posted.Id, ct);
        }

        _panelShown = shown;
    }

    private string _panelShown = "";

    private const int AuditBatch = 50;

    // Every new gm_audit entry, in order, to the audit channel. The first read only marks where the trail stands; the
    // position is saved once Discord took the lines, so nothing is lost or repeated across restarts.
    private async Task WatchGmAuditAsync(CancellationToken ct)
    {
        if (settings.Discord.Channels.GmAudit == 0 || _client.GetChannel(settings.Discord.Channels.GmAudit) is not IMessageChannel channel)
        {
            return;
        }

        if (_state.GmAudit is not { } after)
        {
            var newest = await gameMasters.RecentAuditAsync(1, ct);
            await SaveStateAsync(state => state.GmAudit = newest.Count > 0 ? newest[0].Id : 0, ct);
            return;
        }

        var entries = await gameMasters.AuditAfterAsync(after, AuditBatch, ct);
        if (entries.Count == 0)
        {
            return;
        }

        await SendMessagesAsync(channel, BotTexts.Pack(entries.Select(entry => BotTexts.AuditLine(entry, TimeZoneInfo.Local))));
        await SaveStateAsync(state => state.GmAudit = entries[^1].Id, ct);
    }

    private async Task WatchEventsAsync(CancellationToken ct)
    {
        var calendar = await data.CalendarAsync(ct);
        var started = _events.Started(calendar);
        await SendAsync(settings.Discord.Channels.Announcements,
            started.Select(occurrence => BotTexts.EventStarted(occurrence, calendar.Calendar.BossesOf(occurrence), calendar.Zone)), ping: true);
    }

    private async Task WatchNewsAsync(CancellationToken ct)
    {
        var items = await site.NewsAsync(ct);
        var firstRead = _state.News is null;
        var fresh = NewsWatch.Fresh(_state, items);
        await SendAsync(settings.Discord.Channels.Announcements, fresh.Select(BotTexts.NewsPost), ping: true);
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
