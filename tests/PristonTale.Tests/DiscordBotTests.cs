using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Support;
using PristonTale.Contracts.Social;
using PristonTale.DiscordBot;
using PristonTale.DiscordBot.Sources;
using PristonTale.Server.Net;
using PristonTale.Simulation.Social;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Loot;
using PristonTale.Web.Services;

namespace PristonTale.Tests;

internal static class DiscordBotTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("discord: a remote game master runs only the commands that need no character, at its level, and is audited", RemoteGameMasterAsync);
        yield return ("discord: Discord lines reach every player on the trade channel with their prefix, on one line and cut", ExternalChatAsync);
        yield return ("discord: the bot link answers only with its key, relays chat, runs remote commands and caps the level", BotLinkAsync);
        yield return ("discord: the bot link's settings refuse a non-loopback address, a short key and a bad level", BotLinkSettingsAsync);
        yield return ("discord: the activity cursor skips the backlog once, follows its stream and reads a new run from the start", ActivityCursorAsync);
        yield return ("discord: worlds are announced after two agreeing readings, events and news once each", WatchesAsync);
        yield return ("discord: texts escape the game's words, pack within Discord's limit and word every outcome", TextsAsync);
        yield return ("discord: every /gm subcommand runs a remote command, levels come from roles and tickets keep their ids", ActionsAsync);
        yield return ("discord: the bot's settings and command line are checked before it starts", SettingsAsync);
    }

    private static async Task RemoteGameMasterAsync()
    {
        var fixture = new ChatFixture();
        var target = await fixture.JoinAsync("Target", 0, 0);
        var moderator = new RemoteGameMaster("discord:42", "Ana", GameMasterLevel.Moderator);

        var kick = await fixture.GameMasters.HandleRemoteAsync(moderator, "/kick Target");
        Assert.True(kick.Outcome == GmCommandOutcome.Done && target.Link.Disconnected is not null, "a remote moderator kicks a player");
        Assert.True(kick.Replies.Single().Contains("Target", StringComparison.Ordinal), "the kick's reply comes back in the result");

        var near = await fixture.GameMasters.HandleRemoteAsync(moderator, "/near Target");
        Assert.True(near.Outcome == GmCommandOutcome.NeedsWorld && near.Replies.Count == 0, "a command that moves the issuer needs a character");

        var guide = new RemoteGameMaster("discord:7", "Bia", GameMasterLevel.Guide);
        var denied = await fixture.GameMasters.HandleRemoteAsync(guide, "/>hello");
        Assert.True(denied.Outcome == GmCommandOutcome.Denied && denied.Replies.Single().Contains("needs game-master level 2", StringComparison.Ordinal),
            "a guide is told the level a notice needs");

        var notice = await fixture.GameMasters.HandleRemoteAsync(moderator, "/>Server restart at 10");
        Assert.True(notice.Outcome == GmCommandOutcome.Done, "a remote moderator sends a notice");
        Assert.True((await fixture.GameMasters.HandleRemoteAsync(moderator, "/unknown")).Outcome == GmCommandOutcome.Unknown, "an unknown line matches nothing");
        Assert.True((await fixture.GameMasters.HandleRemoteAsync(moderator, "/user_limit 50")).Outcome == GmCommandOutcome.Done && fixture.Server.UserLimit == 50,
            "a remote moderator sets the player limit");

        var audited = fixture.Audit.Entries.Where(static entry => entry.UserId.StartsWith("discord:", StringComparison.Ordinal)).ToArray();
        Assert.True(audited.Length == 5 && audited.All(static entry => entry.AccountId == 0), "every matched remote attempt is audited under the Discord user");
        Assert.True(audited.Any(static entry => entry.Command == "/near Target" && !entry.Accepted), "a refused remote command is audited too");
        Assert.True(GameMasterCommands.RemoteCommands.Contains("/kick") && !GameMasterCommands.RemoteCommands.Contains("/near"),
            "the remote list holds the commands without an issuer in the world");
    }

    private static async Task ExternalChatAsync()
    {
        var fixture = new ChatFixture();
        var player = await fixture.JoinAsync("Reader", 0, 0);
        var flood = new ExternalChatFlood();
        Assert.True(ExternalChat.Relay(fixture.Directory, flood, 0, ExternalChat.DiscordSource, "Ana\nMaria", "oi\r\n  pessoal") == ExternalChatOutcome.Sent,
            "a line is relayed");
        Assert.True(player.Link.Lines.Last() is { Kind: ChatKind.Trade, Text: "[Discord] Ana Maria: oi pessoal" }, "it reaches players as one trade line");
        Assert.True(fixture.Directory.RecentPublicMessages().Last().Text == "[Discord] Ana Maria: oi pessoal",
            "the trade line enters the public activity stream");
        Assert.True(ExternalChat.IsFrom(ExternalChat.DiscordSource, "[Discord] Ana Maria: oi pessoal") && !ExternalChat.IsFrom(ExternalChat.DiscordSource, "Ana: [Discord]"),
            "the bridge recognises its own lines only by their start");

        var line = ExternalChat.Compose(ExternalChat.DiscordSource, new string('n', 40), new string('x', 200));
        Assert.True(line == "[Discord] " + new string('n', ExternalChat.AuthorLimit) + ": " + new string('x', ChatRules.InputLimit),
            "the author and the text are cut to their limits");
        Assert.True(ExternalChat.Relay(fixture.Directory, flood, 0, ExternalChat.DiscordSource, "Ana", " \n\t ") == ExternalChatOutcome.Empty,
            "an empty text is not relayed");
        Assert.True(ExternalChat.Relay(fixture.Directory, flood, 100, ExternalChat.DiscordSource, "Ana\nMaria", "oi pessoal") == ExternalChatOutcome.Flooded,
            "the same line again at once is refused, as for a player");
        var outcomes = Enumerable.Range(0, ChatRules.FloodLines + 2)
            .Select(index => ExternalChat.Relay(fixture.Directory, flood, 10_000 + index * 100, ExternalChat.DiscordSource, "Bia", "linha " + index))
            .ToArray();
        Assert.True(outcomes.Take(ChatRules.FloodLines + 1).All(static outcome => outcome == ExternalChatOutcome.Sent)
            && outcomes[^1] == ExternalChatOutcome.Flooded, "an author sending too fast is frozen by the players' flood rules");
        Assert.True(ExternalChat.Relay(fixture.Directory, flood, 11_000, ExternalChat.DiscordSource, "Caio", "oi") == ExternalChatOutcome.Sent,
            "another author is not held by it");
        Assert.True(ExternalChat.Compose(ExternalChat.DiscordSource, "Ana", new string('a', ChatRules.InputLimit - 1) + "😀").EndsWith('a'),
            "a character is never cut in half");
    }

    private static async Task BotLinkAsync()
    {
        var fixture = new ChatFixture();
        var player = await fixture.JoinAsync("Target", 0, 0);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BotLink:Enabled"] = "true",
            ["BotLink:Url"] = "http://127.0.0.1:0",
            ["BotLink:Key"] = Key,
            ["BotLink:MaxGmLevel"] = "2"
        }).Build();
        await using var link = new BotLinkService(configuration, fixture.Directory, fixture.GameMasters, NullLogger<BotLinkService>.Instance);
        await link.StartAsync(CancellationToken.None);
        using var http = new HttpClient { BaseAddress = link.Address };

        using (var anonymous = await http.PostAsJsonAsync(BotLinkPaths.Chat, new BotChatRequest("Ana", "oi")))
        {
            Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized, "a request without the key is refused");
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong" + Key[5..]);
        using (var wrong = await http.PostAsJsonAsync(BotLinkPaths.Chat, new BotChatRequest("Ana", "oi")))
        {
            Assert.True(wrong.StatusCode == HttpStatusCode.Unauthorized, "a wrong key is refused");
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        using (var said = await http.PostAsJsonAsync(BotLinkPaths.Chat, new BotChatRequest("Ana", "oi")))
        {
            Assert.True(said.StatusCode == HttpStatusCode.NoContent && player.Link.Last == "[Discord] Ana: oi", "a chat request reaches the players");
        }

        using (var empty = await http.PostAsJsonAsync(BotLinkPaths.Chat, new BotChatRequest("Ana", "")))
        {
            Assert.True(empty.StatusCode == HttpStatusCode.BadRequest, "an empty line is refused");
        }

        using (var repeated = await http.PostAsJsonAsync(BotLinkPaths.Chat, new BotChatRequest("Ana", "oi")))
        {
            Assert.True(repeated.StatusCode == HttpStatusCode.TooManyRequests, "a flooding author is refused with 429");
        }

        var item = await PostCommandAsync(http, new BotCommandRequest("discord:1", "Ana", GameMasterLevel.Administrator, "/@get WA101"));
        Assert.True(item.Outcome == nameof(GmCommandOutcome.NeedsWorld), "a command that needs a character is refused remotely");
        var capped = await PostCommandAsync(http, new BotCommandRequest("discord:1", "Ana", GameMasterLevel.Administrator, "/kick Target"));
        Assert.True(capped.Outcome == nameof(GmCommandOutcome.Done) && player.Link.Disconnected is not null, "the kick runs through the link");
        Assert.True(fixture.Audit.Entries.Last() is { GmLevel: GameMasterLevel.Moderator, UserId: "discord:1" },
            "the claimed level is capped at MaxGmLevel and audited");

        using (var missing = await http.GetAsync(BotLinkPaths.Command))
        {
            Assert.True(missing.StatusCode == HttpStatusCode.MethodNotAllowed, "only posts are served");
        }

        await link.StopAsync(CancellationToken.None);
    }

    private static async Task<BotCommandResponse> PostCommandAsync(HttpClient http, BotCommandRequest request)
    {
        using var response = await http.PostAsJsonAsync(BotLinkPaths.Command, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BotCommandResponse>())!;
    }

    private static Task BotLinkSettingsAsync()
    {
        static BotLinkSettings Read(params (string Key, string Value)[] values) => BotLinkSettings.From(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build());

        Assert.True(!Read().Enabled, "the link is off by default");
        Assert.True(Read(("BotLink:Enabled", "true"), ("BotLink:Key", Key)).MaxGmLevel == GameMasterLevel.Moderator, "the default level cap is Moderator");
        Assert.Throws<InvalidOperationException>(() => Read(("BotLink:Enabled", "true"), ("BotLink:Key", Key), ("BotLink:Url", "http://0.0.0.0:10011")),
            "a link on every address is refused");
        Assert.Throws<InvalidOperationException>(() => Read(("BotLink:Enabled", "true"), ("BotLink:Key", "short")), "a short key is refused");
        Assert.Throws<InvalidOperationException>(() => Read(("BotLink:Enabled", "true"), ("BotLink:Key", Key), ("BotLink:MaxGmLevel", "5")),
            "a level above Administrator is refused");
        return Task.CompletedTask;
    }

    private static Task ActivityCursorAsync()
    {
        var run = Guid.NewGuid();
        static PublicGameMessage Line(long sequence) => new(sequence, DateTimeOffset.UnixEpoch, "trade", "Ana: " + sequence);

        var cursor = new ActivityCursor();
        Assert.True(Next(cursor, new PublicGameActivity(run, DateTimeOffset.UnixEpoch, 1, [Line(1), Line(2)])).Count == 0, "the first snapshot is backlog");
        var next = Next(cursor, new PublicGameActivity(run, DateTimeOffset.UnixEpoch, 1, [Line(2), Line(3), Line(4)]));
        Assert.True(next.Select(static line => line.Sequence).SequenceEqual([3L, 4L]), "later lines of the stream are new once each");
        Assert.True(Next(cursor, new PublicGameActivity(run, DateTimeOffset.UnixEpoch, 1, [Line(3), Line(4)])).Count == 0, "an unchanged snapshot is nothing new");
        var restarted = Next(cursor, new PublicGameActivity(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1, [Line(1)]));
        Assert.True(restarted.Single().Sequence == 1, "a new server run is read from its first line");

        var resumed = new ActivityCursor(new ActivityPosition(run, 3));
        Assert.True(Next(resumed, new PublicGameActivity(run, DateTimeOffset.UnixEpoch, 1, [Line(3), Line(4)])).Single().Sequence == 4,
            "a saved position resumes where the bot stopped");

        var unsent = resumed.Peek(new PublicGameActivity(run, DateTimeOffset.UnixEpoch, 1, [Line(4), Line(5)]));
        Assert.True(unsent.Lines.Single().Sequence == 5 && resumed.Peek(new PublicGameActivity(run, DateTimeOffset.UnixEpoch, 1, [Line(5)])).Lines.Count == 1,
            "lines not committed after a failed send are offered again");
        return Task.CompletedTask;
    }

    private static IReadOnlyList<PublicGameMessage> Next(ActivityCursor cursor, PublicGameActivity snapshot)
    {
        var (lines, position) = cursor.Peek(snapshot);
        cursor.Commit(position);
        return lines;
    }

    private static Task WatchesAsync()
    {
        static WorldReport World(bool online) =>
            new(new WorldInfo("Noxius", "BR", "Normal", "wss://localhost:11010/game", "https://localhost:11010/status"), online ? new WorldLoad(3, 100) : null);

        var worlds = new WorldWatch();
        Assert.True(worlds.Changes([World(true)]).Count == 0, "the first reading sets the state");
        Assert.True(worlds.Changes([World(false)]).Count == 0, "one failed reading is not a fall");
        Assert.True(worlds.Changes([World(true)]).Count == 0, "a recovered reading cancels it");
        Assert.True(worlds.Changes([World(false)]).Count == 0 && worlds.Changes([World(false)]).Single() is { Online: false },
            "two failed readings in a row announce the fall");
        Assert.True(worlds.Changes([World(false)]).Count == 0, "a fall is announced once");

        var now = new DateTimeOffset(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
        var bellatra = new CalendarOccurrence(CalendarEventKind.Bellatra, now.AddMinutes(30), now.AddHours(2));
        SiteCalendar At(DateTimeOffset moment) =>
            SiteCalendar.Build(new EventCalendar([], [bellatra], 0, DropEvents.None), [], moment, TimeZoneInfo.Utc);
        var events = new EventWatch();
        Assert.True(events.Started(At(now)).Count == 0 && events.Started(At(now.AddMinutes(10))).Count == 0, "nothing runs yet");
        Assert.True(events.Started(At(now.AddMinutes(31))).Single().Kind == CalendarEventKind.Bellatra, "an event that starts is announced");
        Assert.True(events.Started(At(now.AddMinutes(40))).Count == 0, "and only once");
        var late = new EventWatch();
        Assert.True(late.Started(At(now.AddMinutes(40))).Count == 0, "an event already running when the bot starts is not announced");

        var state = new BotState();
        var first = new NewsItem(new DateOnly(2026, 11, 1), "Abertura", "", []);
        var second = new NewsItem(new DateOnly(2026, 11, 10), "Evento", "", []);
        Assert.True(NewsWatch.Fresh(state, [first]).Count == 0, "the first read announces nothing");
        NewsWatch.Mark(state, [first]);
        Assert.True(NewsWatch.Fresh(state, [second, first]).Single() == second, "a new post is announced");
        Assert.True(NewsWatch.Fresh(state, [second, first]).Count == 1, "a post not marked as sent is offered again");
        NewsWatch.Mark(state, [second]);
        Assert.True(NewsWatch.Fresh(state, [second, first]).Count == 0 && state.News!.Count == 2, "a sent post is announced once");
        return Task.CompletedTask;
    }

    private static Task TextsAsync()
    {
        var line = BotTexts.ChatLine(new PublicGameMessage(1, DateTimeOffset.UnixEpoch, "clan", "Ana> **vende** @everyone"));
        Assert.True(line == @"**[Clã]** Ana\> \*\*vende\*\* @everyone", $"the game's words are escaped and the channel is named ({line})");

        var packed = BotTexts.Pack(Enumerable.Repeat(new string('a', 900), 5).Append(new string('b', 2500)));
        Assert.True(packed.All(static message => message.Length <= BotTexts.MessageLimit) && packed.Count == 4,
            "lines are joined into messages within the limit and a long line is cut");

        var ticket = new SupportTicket(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Item sumiu", "Perdi minha\narma")
        {
            Category = "Bug",
            AuthorName = "ana (Zephira)",
            Messages = [new TicketMessage(DateTimeOffset.UnixEpoch, TicketAuthor.Player, "ana (Zephira)", "Perdi minha\narma"),
                new TicketMessage(DateTimeOffset.UnixEpoch.AddHours(1), TicketAuthor.Staff, "GM Bia", "Devolvida")]
        };
        var header = BotTexts.TicketHeader(9, ticket, TimeZoneInfo.Utc);
        Assert.True(header.Contains($"#{ticket.Number} · Bug", StringComparison.Ordinal) && header.Contains("conta #9", StringComparison.Ordinal)
            && header.Contains("> Perdi minha\n> arma", StringComparison.Ordinal) && header.Contains("**Respondido**", StringComparison.Ordinal),
            $"a ticket's post shows its number, category, account, state and first message ({header})");
        Assert.True(BotTexts.TicketMessage(ticket.Messages[1], TimeZoneInfo.Utc).StartsWith("🛡️ **GM Bia**", StringComparison.Ordinal),
            "a staff message is marked as the staff's");
        var transcript = BotTexts.TicketTranscript(9, ticket, TimeZoneInfo.Utc);
        Assert.True(transcript.Contains("Jogador — ana (Zephira)\nPerdi minha\narma", StringComparison.Ordinal)
            && transcript.Contains("Equipe — GM Bia\nDevolvida", StringComparison.Ordinal), "the transcript holds the whole conversation");
        Assert.True(BotTexts.TicketThreadName(ticket with { Subject = new string('s', 200) }).Length <= BotTexts.ThreadNameLimit, "a thread name fits Discord");

        Assert.True(BotTexts.GameMasterResult(new BotCommandResponse("Done", [])) == "✅ Feito.", "a silent command is done");
        Assert.True(BotTexts.GameMasterResult(new BotCommandResponse("Done", ["> a ```b"])).Contains("'''", StringComparison.Ordinal),
            "replies cannot close their code block");
        Assert.True(BotTexts.GameMasterResult(new BotCommandResponse("NeedsWorld", [])).StartsWith('⛔'), "a command that needs the game is refused");
        Assert.True(BotTexts.Status([]) == "Nenhum mundo configurado no site.", "no world has its own text");
        Assert.True(BotTexts.Online(new WorldLoad(1234, 0)) == "1.234 online", "numbers use the Portuguese grouping");
        Assert.True(BotTexts.Cut("ab😀c", 4) == "ab…" && BotTexts.Cut("abc", 3) == "abc", "a cut never splits a character");
        var reply = BotTexts.GameMasterResult(new BotCommandResponse("Done", Enumerable.Repeat(new string('r', 100), 40).ToArray()));
        Assert.True(reply.Length <= BotTexts.MessageLimit && reply.EndsWith("```", StringComparison.Ordinal) && BotTexts.Pack(reply.Split('\n')).Count == 1,
            "long replies stay in one closed code block within one message");
        return Task.CompletedTask;
    }

    private static Task ActionsAsync()
    {
        foreach (var action in GameMasterActions.All)
        {
            var line = action.Line(action.Argument is null ? null : "x");
            Assert.True(GameMasterCommands.RemoteCommands.Any(command => ChatCommandText.Match(command, line) > 0),
                $"/gm {action.Name} runs a remote command ({line})");
        }

        Assert.True(GameMasterActions.Find("aviso")!.Line(" Oi ") == "/>Oi" && GameMasterActions.Find("kick")!.Line("Ana") == "/kick Ana",
            "arguments follow the command as the game reads them");
        Assert.True(GameMasterActions.Find("limite")!.Line(null) == "/user_limit", "an optional argument may be left out");
        var roles = new Dictionary<string, int> { ["10"] = 1, ["20"] = 3 };
        Assert.True(GameMasterActions.LevelOf([10UL, 20UL, 30UL], roles) == 3 && GameMasterActions.LevelOf([30UL], roles) == 0,
            "a member acts with its highest role's level");

        var id = Guid.NewGuid();
        Assert.True(DiscordBotService.ParseTicket("ticket-reply:12:" + id.ToString("N"), "ticket-reply") == (12L, id), "a ticket button keeps its ids");
        Assert.True(DiscordBotService.ParseTicket("ticket-reply:-1:" + id.ToString("N"), "ticket-reply") is null
            && DiscordBotService.ParseTicket("other:12:" + id.ToString("N"), "ticket-reply") is null, "another component is not a ticket");
        return Task.CompletedTask;
    }

    private static Task SettingsAsync()
    {
        Assert.True(new BotSettings().Problems(connecting: false).Count == 0, "the defaults are usable for --check");
        var connecting = new BotSettings().Problems(connecting: true);
        Assert.True(connecting.Count == 2, "connecting needs a token and a guild");
        var wrong = new BotSettings
        {
            Site = new SiteSettings { BaseUrl = "http://example.com" },
            GameLink = new GameLinkSettings { Url = "http://192.0.2.1:10011" },
            Discord = new DiscordSettings { GameMasterRoles = new Dictionary<string, int> { ["admins"] = 9 } }
        }.Problems(connecting: false);
        Assert.True(wrong.Count == 4, $"plain http, a remote link without key and a bad role are refused ({string.Join(" ", wrong)})");

        var line = BotCommandLine.Parse(["--settings", "bot.json", "--Discord:GuildId=5", "--check", "ranking", "mago"]);
        Assert.True(line.Error is null && line.SettingsPath == Path.GetFullPath("bot.json") && line.Check.SequenceEqual(["ranking", "mago"])
            && line.Overrides.SequenceEqual(["--Discord:GuildId=5"]), "the bot's own arguments are taken out and the rest are settings");
        var after = BotCommandLine.Parse(["--check", "status", "--Site:BaseUrl=https://example.com"]);
        Assert.True(after.Check.SequenceEqual(["status"]) && after.Overrides.SequenceEqual(["--Site:BaseUrl=https://example.com"]),
            "a setting after the check command is still a setting");
        Assert.True(BotCommandLine.Parse(["--check"]).Error is not null && BotCommandLine.Parse(["--settings"]).Error is not null,
            "a missing value is an error");
        return Task.CompletedTask;
    }
}
