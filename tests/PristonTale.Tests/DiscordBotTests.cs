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
using PristonTale.Persistence.Abstractions.GameMasters;
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

        var feat = new GameFeat(GameFeat.Pvp, "Castelo", "Bia", 90, [new FeatPlayer("Ana", 95, PristonTale.Contracts.Characters.PlayerClass.Knight, 0, "")], null, []);
        fixture.Directory.RecordFeat(feat);
        Assert.True(fixture.Directory.RecentPublicMessages().Last() is { Channel: GameFeat.Pvp, Text: "Bia morto por Ana em Castelo" } recorded && recorded.Feat == feat,
            "a feat enters the public activity with its details and a readable line");

        ServerNotices.Broadcast(fixture.Directory, "Manutenção às 20h");
        Assert.True(fixture.Directory.RecentPublicMessages().Last() is { Channel: "notice", Text: "Manutenção às 20h" },
            "a game master's notice enters the public activity stream without its prefix");
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
        Assert.True(line == "[Clã] Ana> **vende** @everyone", $"the channel is named and the game's words are kept as written ({line})");
        var named = BotTexts.ChatLine(new PublicGameMessage(1, DateTimeOffset.UnixEpoch, "local", "Ana: oi ```x"), "Avalon");
        Assert.True(named == "[Avalon · Local] Ana: oi '''x", $"a line of one of several servers names its server and cannot close its block ({named})");
        var blocks = BotTexts.ChatBlocks(Enumerable.Repeat(new string('c', 900), 3).Prepend(line));
        Assert.True(blocks.Count == 2 && blocks.All(static block => block.StartsWith("```\n", StringComparison.Ordinal) && block.EndsWith("\n```", StringComparison.Ordinal)
            && block.Length <= BotTexts.MessageLimit), "chat lines go to Discord as code blocks within the message limit, where nothing formats or pings");

        Assert.True(new BotSettings().Discord.PingEveryone && BotTexts.Pack([BotTexts.Everyone, "🟢 O mundo **Avalon** está no ar."]).Single().StartsWith("@everyone\n", StringComparison.Ordinal),
            "announcements ping everyone by default, once, before the batch");
        var titles = new ClassTitles(new Dictionary<string, string> { ["Class.Knight.0"] = "Cavaleiro" });
        var strong = new FeatPlayer("DEV Strong", 120, PristonTale.Contracts.Characters.PlayerClass.Knight, 0, "Noxius");
        var bia = new FeatPlayer("Bia", 118, PristonTale.Contracts.Characters.PlayerClass.Priestess, 0, "");
        var boss = FeatCards.For(new GameFeat(GameFeat.Boss, "Vale", "Valento", 100, [strong, bia], null, ["Espada x2", "1.000 de ouro"]), "", titles, 1);
        Assert.True(boss.Title == "☠️ Valento foi derrotado!" && boss.Description.Contains("o grupo de **DEV Strong**", StringComparison.Ordinal)
            && boss.Fields.Any(static field => field.Name == "🛡️ Grupo (2)" && field.Value.Contains("Bia", StringComparison.Ordinal))
            && boss.Fields.Any(static field => field.Name == "💰 Drops" && field.Value == @"• Espada x2" + "\n" + @"• 1\.000 de ouro")
            && boss.Fields[0].Value == "**DEV Strong**\nCavaleiro · nv. 120\n🏰 Noxius", $"a boss card names the killer, the party and the drops ({boss.Description})");
        var duel = FeatCards.For(new GameFeat(GameFeat.Pvp, "Castelo", "Bia", 118, [strong with { Level = 100 }], bia, []), "Avalon", titles, 0);
        Assert.True(duel.Title == "⚔️ DEV Strong abateu Bia" && duel.Description.Contains("18 níveis", StringComparison.Ordinal) && duel.Footer == "Avalon · PvP",
            $"a PvP card names both sides and calls out a kill against the odds ({duel.Description})");
        Assert.True(new ChannelSettings { BossKills = 5, PvpKills = 6 }.FeatChannel(GameFeat.Boss) == 5 && new ChannelSettings { PvpKills = 6 }.FeatChannel(GameFeat.Pvp) == 6,
            "each feat has its feed");
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

        Assert.True(BotTexts.DevNoteTitle("  Chat do Discord ") == "🛠️ Chat do Discord" && BotTexts.DevNoteText(@"Linha 1\nLinha 2") == "Linha 1\nLinha 2",
            "a development note's title is marked and \\n breaks its text's lines");
        Assert.True(BotTexts.DevNoteSignature("DEV Strong", new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc) == "DEV Strong · 18/11/2026",
            "a development note is signed with the member's name and the day");
        Assert.True(SlashCommands.ImageOptions.SequenceEqual(["imagem1", "imagem2", "imagem3", "imagem4"]), "a development note takes up to four images");
        var note = DevNote.Parse("2026-11-18-chat", "\n# Novo sistema: Chat\n\nResumo em\nduas linhas.\n\nCanais:\n- Troca\n- Clã\n\nFim.", DateTime.UnixEpoch)!;
        Assert.True(note.Date == new DateOnly(2026, 11, 18) && note.Title == "Novo sistema: Chat" && note.Summary == "Resumo em\nduas linhas."
            && note.Body.SequenceEqual(["Canais:\n- Troca\n- Clã", "Fim."]), "a note's folder gives its day, its first line the title and its paragraphs the text");
        Assert.True(DevNote.Parse("sem-data", "Sem título", DateTime.UnixEpoch) is null && DevNote.Parse("x", "# T", DateTime.UnixEpoch)!.Date == new DateOnly(1970, 1, 1),
            "a note needs a title; without a day in its name it takes its file's");
        Assert.True(note.Kind == DevNoteKind.System && DevNote.Parse("2026-11-19-x", "# Chat mais rápido\nTipo: melhoria\n\nResumo.", DateTime.UnixEpoch) is { Kind: DevNoteKind.Improvement, Summary: "Resumo." }
            && DevNote.Parse("2026-11-19-y", "# Painel\nTipo: Equipe\n\nResumo.", DateTime.UnixEpoch) is { Kind: DevNoteKind.Staff }
            && BotTexts.DevNoteTitle("Chat", DevNoteKind.Improvement) == "✨ Nova melhoria: Chat" && BotTexts.DevNoteTitle("Painel", DevNoteKind.Staff) == "🧰 Ferramenta da equipe: Painel",
            "an improvement and a staff tool are marked by their line, which stays out of the text");
        var improvement = DevNote.Parse("2026-11-19-x", "# Chat mais rápido\nTipo: melhoria\n\nResumo.", DateTime.UnixEpoch)!;
        var staff = DevNote.Parse("2026-11-19-y", "# Painel\nTipo: equipe\n\nResumo.", DateTime.UnixEpoch)!;
        Assert.True(BotTexts.InNews(note) && BotTexts.InNews(improvement) && !BotTexts.InNews(staff)
            && BotTexts.DevNoteNews(improvement, "DEV Strong").Kind == NewsKind.Improvement && BotTexts.DevNoteNews(note, "DEV Strong").Kind == NewsKind.News,
            "new systems and improvements reach the game's news with their badge, staff tools never");
        var state = new BotState { News = [] };
        Assert.True(NewsWatch.Fresh(state, [BotTexts.DevNoteNews(improvement, ""), BotTexts.DevNoteNews(note, "")]).Single().Kind == NewsKind.News,
            "an improvement in the news is not announced in the announcements channel");
        var post = BotTexts.DevNoteNews(note, "DEV Strong");
        Assert.True(post.Summary == "Resumo em duas linhas." && post.Body.SequenceEqual(["Canais:", "- Troca", "- Clã", "", "Fim."]) && post.Author == "DEV Strong",
            "a note becomes a signed news post with a line per note line");

        Assert.True(BotTexts.GameMasterResult(new BotCommandResponse("Done", [])) == "✅ Feito.", "a silent command is done");
        Assert.True(BotTexts.GameMasterResult(new BotCommandResponse("Done", ["> a ```b"])).Contains("'''", StringComparison.Ordinal),
            "replies cannot close their code block");
        Assert.True(BotTexts.GameMasterResult(new BotCommandResponse("NeedsWorld", [])).StartsWith('⛔'), "a command that needs the game is refused");
        Assert.True(BotTexts.Status([]) == "Nenhum mundo configurado no site.", "no world has its own text");
        var world = new WorldInfo("Noxius", "Brasil", "Normal", "wss://localhost:11010/game", "https://localhost:11010/status");
        var up = BotTexts.StatusPanel([new WorldReport(world, new WorldLoad(12, 6000))]);
        var down = BotTexts.StatusPanel([new WorldReport(world, null)]);
        Assert.True(up.Color == BotTexts.StatusUpColor && up.Fields.Single() is { Name: "🟢 Noxius · Brasil", Value: "No ar · 12 de 6.000 online" }
            && down.Color == BotTexts.StatusDownColor && down.Fields.Single().Value == "Fora do ar", "the status panel shows each world up or down with its players");
        var at = new DateTime(2026, 11, 19, 3, 30, 0, DateTimeKind.Unspecified);
        var kick = BotTexts.AuditLine(new GmAuditRecord(7, 1, "strong", "DEV Strong", 4, "/kick Fulano", 2, true, "Kicked Fulano", at), TimeZoneInfo.Utc);
        var refused = BotTexts.AuditLine(new GmAuditRecord(8, 0, "discord:42", "Bia", 1, "discord: ticket close x", 1, false, "", at), TimeZoneInfo.Utc);
        var site = BotTexts.AuditLine(new GmAuditRecord(9, 1, "strong", "", 4, "web: correio", 4, true, "#3 · Todos", at), TimeZoneInfo.Utc);
        Assert.True(BotTexts.AuditLine(new GmAuditRecord(10, 0, "discord:42", "Bia", 2, "/kick Ana", 2, true, "", at), TimeZoneInfo.Utc).StartsWith("💬", StringComparison.Ordinal),
            "a game command sent from Discord is marked as Discord's");
        Assert.True(kick == "🎮 `19/11 03:30` **DEV Strong** (nv. 4) · `/kick Fulano` ✅ — Kicked Fulano" && refused.StartsWith("💬", StringComparison.Ordinal)
            && refused.EndsWith("⛔", StringComparison.Ordinal) && site.StartsWith("🌐", StringComparison.Ordinal) && site.Contains("**strong**", StringComparison.Ordinal),
            $"an audit line shows where, when, who, the command and its outcome ({kick} | {refused} | {site})");
        var updating = BotTexts.StatusPanel([new WorldReport(world, null)], maintenance: true, "Nova versão");
        Assert.True(updating.Color == BotTexts.StatusMaintenanceColor && updating.Fields.Single() is { Name: "🟡 Noxius · Brasil", Value: "Em atualização" }
            && updating.Description.Contains("Nova versão", StringComparison.Ordinal) && BotTexts.MaintenanceChanged(false, "").StartsWith("🟢", StringComparison.Ordinal),
            "maintenance turns the panel yellow with its reason, and its end is announced");
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

        Assert.True(DiscordBotService.ParsePurge(DiscordBotService.PurgeId(1572815838598135818, 50)) == (1572815838598135818UL, 50)
            && DiscordBotService.ParsePurge("purge:1:0") is null && DiscordBotService.ParsePurge("purge:1:" + (SlashCommands.PurgeLimit + 1)) is null
            && BotTexts.PurgeQuestion(5, 1000, all: true).Contains("todas as mensagens", StringComparison.Ordinal), "a purge button keeps its channel and a count within the limit");
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

        var single = new BotSettings { GameLink = new GameLinkSettings { Url = "http://127.0.0.1:10011", Key = Key } };
        Assert.True(single.GameServers.Single() is { Name: "" } only && only.GameLink.Url == single.GameLink.Url,
            "without Servers the bot follows the one server of GameLink and Activity");
        var unnamed = new BotSettings { Servers = [new GameServerSettings { Name = "Avalon" }, new GameServerSettings { Name = "avalon " }, new GameServerSettings()] };
        Assert.True(unnamed.Problems(connecting: false).Count == 1, "every server needs a name of its own");
        var badLink = new BotSettings { Servers = [new GameServerSettings { Name = "Avalon", GameLink = new GameLinkSettings { Url = "http://127.0.0.1:1" } }] };
        Assert.True(badLink.Problems(connecting: false).Single().Contains("Avalon", StringComparison.Ordinal), "each server's link is checked");
        Assert.True(new ChannelSettings { Announcements = 1, WorldStatus = 2 }.WorldStatusChannel == 2 && new ChannelSettings { Announcements = 1 }.WorldStatusChannel == 1,
            "worlds going down and up have their own channel, else the announcements");
        var channels = new ChannelSettings { PrivateChat = 1, ClanChat = 2 };
        Assert.True(channels.PrivateChannel("clan") == 2 && channels.PrivateChannel("whisper") == 1 && channels.PrivateChannel("party") == 1
            && new ChannelSettings { PrivateChat = 1 }.PrivateChannel("clan") == 1, "clan lines have their own channel, else the private one");
        Assert.True(DiscordBotService.StreamKey("public", "") == "public" && DiscordBotService.StreamKey("public", "Avalon") == "public:Avalon",
            "the only server keeps its relay position; named servers have their own");

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
