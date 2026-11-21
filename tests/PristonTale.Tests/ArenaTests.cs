using System.Numerics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Arena;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Calendar;

namespace PristonTale.Tests;

// The arena (ArenaRules): the rule, the queue and its refusals, a match from the pairing to the return, the time limit
// and leaving, the week's points and prizes in a disposable database, the calendar, the wire and the chat filter.
internal static class ArenaTests
{
    private const int Fighting = 110;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("arena rules: bands from level 100, open hours, the outcome by players standing then damage, the mode names and Monday", RulesAsync);
        yield return ("the arena queue refuses a closed arena, a low level and a 3x3 without a party of three, and /arena sair leaves it", QueueAsync);
        yield return ("a 1x1 match pairs two queued players, fights only them, ends at 1 life without a death and sends both back", MatchAsync);
        yield return ("an arena match ends by damage at the time limit, and a player who leaves the world loses it", EndsAsync);
        yield return ("an arena match sends back to where the player queued from, leaves one who left the field where it went, drops a hidden "
            + "player from the queue and refuses queuing from the quest arena", ReturnsAsync);
        yield return ("the week's arena points and its prizes mailed once per band are stored", StorageAsync);
        yield return ("the arena reaches the calendar, the settings, the wire and the chat filter", WireAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(ArenaRules.Band(99) == -1 && ArenaRules.Band(100) == 0 && ArenaRules.Band(119) == 0 && ArenaRules.Band(120) == 1 && ArenaRules.Band(140) == 2,
            "the level bands differ");
        var mask = EventSchedule.ParseHours("13, 19,22");
        Assert.True(mask == (1 << 13 | 1 << 19 | 1 << 22) && EventSchedule.ParseHours("") == 0 && EventSchedule.ParseHours("24") is null && EventSchedule.ParseHours("13,13") is null,
            "the arena hours were not read");
        var at = new DateTimeOffset(2026, 11, 20, 13, 29, 0, TimeSpan.Zero);
        Assert.True(ArenaRules.IsOpen(mask!.Value, at) && !ArenaRules.IsOpen(mask.Value, at.AddMinutes(1)) && !ArenaRules.IsOpen(mask.Value, at.AddHours(1)),
            "the arena's opening differs");
        Assert.True(ArenaRules.Outcome(1, 0, 0, 99, false) == ArenaOutcome.Won && ArenaRules.Outcome(0, 2, 99, 0, false) == ArenaOutcome.Lost
            && ArenaRules.Outcome(0, 0, 0, 0, false) == ArenaOutcome.Draw && ArenaRules.Outcome(1, 1, 5, 9, false) is null
            && ArenaRules.Outcome(1, 1, 5, 9, true) == ArenaOutcome.Lost && ArenaRules.Outcome(1, 1, 9, 9, true) == ArenaOutcome.Draw, "the outcome differs");
        Assert.True(ArenaRules.ModeOf("3X3") == ArenaMode.Trio && ArenaRules.ModeOf(ArenaRules.ModeName(ArenaMode.Solo)) == ArenaMode.Solo && ArenaRules.ModeOf("2x2") is null,
            "the mode names differ");
        Assert.True(ArenaRules.WeekStart(new DateOnly(2026, 11, 22)) == new DateOnly(2026, 11, 16) && ArenaRules.WeekStart(new DateOnly(2026, 11, 16)) == new DateOnly(2026, 11, 16)
            && ArenaRules.WeekKey(new DateOnly(2026, 11, 16)) == 20261116, "the week differs");
        Assert.True(ArenaRules.Place(0, 0) != ArenaRules.Place(0, 1) && Vector3.Distance(ArenaRules.Place(1, 1), ArenaRules.SecondSide) < 1f, "the places differ");
        return Task.CompletedTask;
    }

    private static async Task<ChatFixture> OpenAsync()
    {
        var fixture = new ChatFixture(clock: new SpecialItemTests.ManualClock());
        await fixture.RunAsync(fixture.World.ApplyOptionsAsync(fixture.World.Options with
        {
            Arena = fixture.World.Options.Arena with { Hours = (1 << 24) - 1 },
            Royale = fixture.World.Options.Royale with { Hours = 0 }
        }));
        fixture.Tick(1);
        return fixture;
    }

    private static async Task QueueAsync()
    {
        var closed = new ChatFixture(clock: new SpecialItemTests.ManualClock());
        await closed.RunAsync(closed.World.ApplyOptionsAsync(closed.World.Options with { Arena = closed.World.Options.Arena with { Hours = 0 } }));
        var early = await closed.JoinAsync("Early", 0, 0, level: Fighting);
        closed.Tick(1);
        Assert.True(await closed.RunAsync(closed.World.JoinArenaAsync(early.Session, ArenaMode.Solo)) == ArenaJoin.Closed, "a closed arena queued");

        var fixture = await OpenAsync();
        Assert.True(fixture.World.TakeTickNotices().Length == 0, "the opening was told before anybody played");
        var rookie = await fixture.JoinAsync("Rookie", 0, 0, level: 60);
        var ann = await fixture.JoinAsync("Ann", 0, 0, level: Fighting);
        Assert.True(await fixture.RunAsync(fixture.World.JoinArenaAsync(rookie.Session, ArenaMode.Solo)) == ArenaJoin.Level, "a low level queued");
        Assert.True(await fixture.RunAsync(fixture.World.JoinArenaAsync(ann.Session, ArenaMode.Trio)) == ArenaJoin.Party, "a 3x3 queued without a party");
        await fixture.SayAsync(ann, "/arena 1x1");
        Assert.True(Body(fixture, ann).ArenaQueued is { Mode: ArenaMode.Solo, Band: 0 }, "the chat command queued nobody");
        Assert.True(await fixture.RunAsync(fixture.World.JoinArenaAsync(ann.Session, ArenaMode.Solo)) == ArenaJoin.Busy, "a queued player queued twice");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(ann)!.Value.Arena is { Queued: true, Mode: ArenaMode.Solo }, "the queue did not reach the snapshot");
        await fixture.SayAsync(ann, "/arena SAIR");
        Assert.True(Body(fixture, ann).ArenaQueued is null
            && await fixture.RunAsync(fixture.World.JoinArenaAsync(ann.Session, null)) == ArenaJoin.NotQueued, "the queue was not left");
    }

    private static async Task<(ChatFixture Fixture, ChatFixture.Player Ann, ChatFixture.Player Bob)> MatchedAsync()
    {
        var fixture = await OpenAsync();
        var ann = await fixture.JoinAsync("Ann", 10, 10, level: Fighting);
        var bob = await fixture.JoinAsync("Bob", 300, 300, level: 115);
        await fixture.SayAsync(ann, "/arena 1x1");
        await fixture.SayAsync(bob, "/arena 1x1");
        fixture.Tick((int)(ArenaRules.PairEveryMs / 50f) + 2);
        return (fixture, ann, bob);
    }

    private static async Task MatchAsync()
    {
        var results = new List<ArenaResult>();
        var (fixture, ann, bob) = await MatchedAsync();
        fixture.World.ArenaResultRecorded = results.Add;
        var cid = await fixture.JoinAsync("Cid", 20, 20, level: Fighting);
        var (annBody, bobBody) = (ArenaBody(fixture, ann), ArenaBody(fixture, bob));
        Assert.True(annBody.Arena is { Side: 0 } seat && bobBody.Arena is { Side: 1 } other && ReferenceEquals(seat.Match, other.Match)
            && annBody.ArenaQueued is null && annBody.Life == annBody.MaxLife, "the queued players were not matched in the arena");
        var arena = ArenaField(fixture);
        Assert.True(arena.SetAttackTarget(ann.Id, bob.Id, sustained: false), "the match's sides could not fight");
        fixture.Tick(1);
        var latest = ChatFixture.Latest(ann)!.Value;
        Assert.True(latest.Arena is { Queued: false, OwnStanding: 1, FoeStanding: 1, SecondsLeft: > 290 }
            && latest.Entities.Single(entity => entity.EntityId == ann.Id).Appearance.ArenaSide == 1, "the match did not reach the snapshot");
        fixture.World.TakeTickNotices();

        // The sides start apart and nobody walks here: the attacker stands by its foe.
        annBody.Teleport(bobBody.Position + new Vector3(20, 0, 0));
        bobBody.ApplyDamage(bobBody.Life - 2);
        Assert.True(arena.SetAttackTarget(ann.Id, bob.Id, sustained: true), "the foe was not taken");
        var notices = new List<WorldNotice>();
        for (var step = 0; step < 300 && annBody.Arena is not null; step++)
        {
            fixture.Tick(20);
            notices.AddRange(fixture.World.TakeTickNotices());
        }

        Assert.True(annBody.Arena is null && bobBody.Arena is null && !bobBody.IsDead && bobBody.Life == bobBody.MaxLife && !bobBody.SlainByPlayer,
            "the match did not end without a death, giving the whole life back");
        Assert.True(ReferenceEquals(Body(fixture, ann), annBody) && Vector2.Distance(new Vector2(annBody.Position.X, annBody.Position.Z), new Vector2(10, 10)) < 1f,
            "the winner did not go back where it queued");
        Assert.True(results is [{ Band: 0, Mode: ArenaMode.Solo, Entries: [{ Name: "Ann", Outcome: ArenaOutcome.Won }, { Name: "Bob", Outcome: ArenaOutcome.Lost }] }],
            "the results were not handed to storage");
        Assert.True(notices.Any(notice => notice.To == ann.Id && notice.Text == GameText.Of(GameTextId.ArenaWon, ArenaRules.WinPoints))
            && notices.Any(notice => notice.To == bob.Id && notice.Text.Id == GameTextId.ArenaOut)
            && notices.Any(notice => notice.To == cid.Id && notice.Text == GameText.Of(GameTextId.ArenaNotice, "1x1", "Ann", "Bob")), "the result was not told");
    }

    private static async Task EndsAsync()
    {
        var (fixture, ann, bob) = await MatchedAsync();
        var results = new List<ArenaResult>();
        fixture.World.ArenaResultRecorded = results.Add;
        ArenaBody(fixture, bob).Arena!.Match.Damage[1] = 50;
        fixture.Tick((int)(ArenaRules.MatchMs / 50f) + 2);
        Assert.True(results is [{ Entries: [{ Name: "Ann", Outcome: ArenaOutcome.Lost }, { Name: "Bob", Outcome: ArenaOutcome.Won }] }],
            "the time limit did not go to the side that dealt more damage");

        var (left, cid, dan) = await MatchedAsync();
        var gone = new List<ArenaResult>();
        left.World.ArenaResultRecorded = gone.Add;
        var leaving = ArenaBody(left, dan);
        await left.RunAsync(left.World.LeaveAsync(ArenaRules.FieldCode, dan.Id, dan.Outbox.Writer));
        left.Tick(1);
        Assert.True(gone is [{ Entries: [{ Name: "Ann", Outcome: ArenaOutcome.Won }, { Name: "Bob", Outcome: ArenaOutcome.Lost }] }] && leaving.Arena is not null
            && Body(left, cid).Arena is null, "the one who stayed did not win");
    }

    private static async Task ReturnsAsync()
    {
        var fixture = await OpenAsync();
        var ann = await fixture.JoinAsync("Ann", 10, 10, level: Fighting);
        var bob = await fixture.JoinAsync("Bob", 300, 300, level: Fighting);
        var eve = await fixture.JoinAsync("Eve", 50, 50, level: Fighting);
        await fixture.SayAsync(ann, "/arena 1x1");
        await fixture.SayAsync(bob, "/arena 1x1");
        await fixture.SayAsync(eve, "/arena 1x1");
        Body(fixture, eve).Hidden = true;
        await fixture.RunAsync(fixture.World.RelocateAsync(ann.Id, ann.Id, new Application.GameMasters.GmPlacement(ChatFixture.Field, new Vector3(80, float.NaN, 80))));
        fixture.Tick((int)(ArenaRules.PairEveryMs / 50f) + 2);
        var (annBody, bobBody) = (ArenaBody(fixture, ann), ArenaBody(fixture, bob));
        Assert.True(annBody.Arena is not null && bobBody.Arena is not null && Body(fixture, eve) is { Arena: null, ArenaQueued: null },
            "a player who hid after queuing was matched or kept in the queue");

        var results = new List<ArenaResult>();
        fixture.World.ArenaResultRecorded = results.Add;
        await fixture.RunAsync(fixture.World.RelocateAsync(bob.Id, bob.Id, new Application.GameMasters.GmPlacement(ChatFixture.Field, new Vector3(200, float.NaN, 200))));
        fixture.Tick(1);
        Assert.True(results is [{ Entries: [{ Name: "Ann", Outcome: ArenaOutcome.Won }, { Name: "Bob", Outcome: ArenaOutcome.Lost }] }]
            && bobBody.Arena is null && Vector2.Distance(new Vector2(bobBody.Position.X, bobBody.Position.Z), new Vector2(200, 200)) < 1f,
            "a player who left the arena's field was not left where it went");
        Assert.True(ReferenceEquals(Body(fixture, ann), annBody) && Vector2.Distance(new Vector2(annBody.Position.X, annBody.Position.Z), new Vector2(10, 10)) < 1f,
            "the winner did not go back to where it queued from");

        // The fixture's catalog has no quest arena to walk into: a player in a match stands there, taken out of it.
        var (other, cid, _) = await MatchedAsync();
        ArenaBody(other, cid).Arena = null;
        Assert.True(await other.RunAsync(other.World.JoinArenaAsync(cid.Session, ArenaMode.Solo)) == ArenaJoin.Busy, "a player queued from the quest arena");
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-arena-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var arena = provider.GetRequiredService<IArenaRepository>();
            var accounts = provider.GetRequiredService<Persistence.Abstractions.Accounts.IAccountRepository>();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var classes = new CharacterClassRegistry();
            var ids = new List<long>();
            foreach (var name in new[] { "Ann", "Bob", "Cid" })
            {
                var account = await accounts.CreateAsync(name.ToLowerInvariant(), "hash");
                ids.Add((await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, name, 0, Contracts.Characters.PlayerClass.Knight, 3))).Id);
            }

            const int week = 20261116;
            await arena.RecordAsync(week, 0, ids[0], true, ArenaRules.WinPoints, ArenaRules.LossPoints);
            await arena.RecordAsync(week, 0, ids[0], true, ArenaRules.WinPoints, ArenaRules.LossPoints);
            await arena.RecordAsync(week, 0, ids[0], false, ArenaRules.WinPoints, ArenaRules.LossPoints);
            await arena.RecordAsync(week, 0, ids[1], false, ArenaRules.WinPoints, ArenaRules.LossPoints);
            await arena.RecordAsync(week, 0, ids[2], true, ArenaRules.WinPoints, ArenaRules.LossPoints);
            Assert.True(await arena.TopAsync(week, 0, 10) is [{ Name: "Ann", Points: 30, Wins: 2, Losses: 1 }, { Name: "Cid", Points: 20 }, { Name: "Bob", Points: 0, Losses: 1 }]
                && (await arena.TopAsync(week, 1, 10)).Count == 0, "the week's points differ");

            var paid = await arena.AwardAsync(week, 0, [1000, 500, 0], "Prêmio da arena", "Parabéns", DateTime.UtcNow);
            Assert.True(paid is [{ Place: 1, Name: "Ann", Gold: 1000 }, { Place: 2, Name: "Cid", Gold: 500 }], "the prizes differ, or a player without points won one");
            Assert.True((await arena.AwardAsync(week, 0, [1000, 500, 0], "Prêmio da arena", "Parabéns", DateTime.UtcNow)).Count == 0
                && (await arena.AwardAsync(week, 1, [1000, 500, 0], "x", "y", DateTime.UtcNow)).Count == 0
                && (await arena.AwardAsync(week, 1, [1000, 500, 0], "x", "y", DateTime.UtcNow)).Count == 0, "a week's band was paid twice");
            await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "world.db")}");
            await connection.OpenAsync();
            var mails = connection.CreateCommand();
            mails.CommandText = "SELECT COUNT(*) FROM game_mail WHERE gold IN (1000, 500) AND recipient_account_id = sender_account_id";
            Assert.True((long)(await mails.ExecuteScalarAsync())! == 2, "the prizes were not mailed");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var rules = new EventScheduleRules { ArenaHours = 1 << 13 };
        var now = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var arena = EventSchedule.Occurrences(rules, now, TimeZoneInfo.Utc, TimeSpan.FromDays(1)).Where(static o => o.Kind == CalendarEventKind.Arena).ToList();
        Assert.True(arena is [{ } first, ..] && first.Start == now.AddHours(13) && first.End == now.AddHours(13).AddMinutes(ArenaRules.OpenMinutes),
            "the arena's opening is not in the calendar");

        var problems = new List<string>();
        var read = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["arena.hours"] = "", ["arena.prize_first"] = "7" }, null, problems);
        var refused = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["arena.hours"] = "25" }, null, problems);
        Assert.True(read.World.Arena is { Hours: 0, FirstPrize: 7 } && refused.World.Arena.Hours == ArenaOptions.Default.Hours && problems.Count == 1,
            "the arena settings were not read or refused");

        var buffer = new byte[ArenaStatePacket.Size];
        var sent = new ArenaStatePacket((byte)ArenaMode.Trio, false, true, 2, 1, 125);
        Assert.True(ArenaStatePacket.Read(buffer.AsSpan(0, sent.Write(buffer))) == sent, "the arena state did not round-trip");
        buffer[12] = 2;
        Assert.Throws<InvalidDataException>(() => ArenaStatePacket.Read(buffer), "an unknown arena mode was read");
        var description = new EntityDescriptionPacket(7, 1, Contracts.World.EntityKind.Player, 110, 1, 1f, "Ann", "",
            new DescribedPlayer(Contracts.Characters.PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, false, false, 0, 0) { ArenaSide = 2 });
        var looks = new byte[EntityDescriptionPacket.MaximumSize];
        Assert.True(EntityDescriptionPacket.Read(looks.AsSpan(0, description.Write(looks))).Player is { ArenaSide: 2, Duelling: false }, "the arena side did not round-trip");

        var notice = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.ArenaNotice, "1x1", "Ann", "Bob"));
        var own = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.ArenaWon, 20));
        var hiding = new ClientSettings { HideArenaNotices = true };
        Assert.True(ChatLineFilter.Hides(hiding, notice) && !ChatLineFilter.Hides(hiding, own) && !ChatLineFilter.Hides(new ClientSettings(), notice),
            "the arena notices were not hidden by the option alone");
        return Task.CompletedTask;
    }

    private static FieldInstance Field(ChatFixture fixture) =>
        fixture.World.TryGetOpenField(ChatFixture.Field, out var field) ? field : throw new InvalidOperationException("the fixture field is not open");

    private static FieldInstance ArenaField(ChatFixture fixture) =>
        fixture.World.TryGetOpenField(ArenaRules.FieldCode, out var field) ? field : throw new InvalidOperationException("the arena field is not open");

    private static WorldPlayer Body(ChatFixture fixture, ChatFixture.Player player) =>
        ((IFieldCombatHost)Field(fixture)).TryGetPlayer(player.Id, out var body) ? body : throw new InvalidOperationException("the player is not in the field");

    private static WorldPlayer ArenaBody(ChatFixture fixture, ChatFixture.Player player) =>
        ((IFieldCombatHost)ArenaField(fixture)).TryGetPlayer(player.Id, out var body) ? body : throw new InvalidOperationException("the player is not in the arena");
}
