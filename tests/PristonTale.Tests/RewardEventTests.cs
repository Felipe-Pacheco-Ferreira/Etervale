using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Events;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Tests;

// The events and rewards window: welcome login days, weekly and daily goals with their resets and level gates, the level
// race, claims into the bag (a full bag keeps the reward), the world's counting and claims, storage and the wire.
internal static class RewardEventTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a login day counts once a day, days need not follow, at most seven count, and none after the campaign", LoginDaysAsync);
        yield return ("a welcome day is claimed once, only when reached, never after the campaign, and a full bag keeps it", WelcomeClaimsAsync);
        yield return ("weekly and daily goals count from their level, reset at Monday and midnight, and pay items and gold", GoalsAsync);
        yield return ("the level race pays once each level reached", LevelRaceAsync);
        yield return ("the world counts entry and midnight as login days, publishes the window and claims through the save gate", WorldAsync);
        yield return ("the reward book survives a SQLite save and load", StorageAsync);
        yield return ("the reward window and its request and result round-trip on the wire within a frame", WireAsync);
    }

    private static readonly CharacterClassRegistry Classes = new();

    // A Monday at noon, as DateOnly day numbers count: day 0 is a Monday.
    private static readonly RewardDate Monday = new(739_571 - 739_571 % 7, 12 * 3600);

    private static readonly ItemCatalog Items = new(RewardEvents.Tracks
        .SelectMany(track => track.Entries)
        .SelectMany(entry => entry.Items)
        .Select(item => RewardEvents.KeyFor(item, PlayerClass.Fighter))
        .Distinct()
        .Select(key => new ItemDefinition(ItemCodes.Parse(key), key, ItemKind.Misc) { CatalogKey = key, Weight = 1 }));

    private static RewardDate Day(int offset) => Monday with { Day = Monday.Day + offset };

    private static Task LoginDaysAsync()
    {
        var book = new RewardEventBook();
        book.CountLogin(Day(0));
        book.CountLogin(Day(0) with { SecondOfDay = 23 * 3600 });
        Assert.True(book.WelcomeDays == 1 && book.WelcomeFirstDay == Day(0).Day, $"one day counted {book.WelcomeDays}");
        book.CountLogin(Day(3));
        book.CountLogin(Day(2));
        Assert.True(book.WelcomeDays == 2, $"a gap or an earlier day counted wrong: {book.WelcomeDays}");
        for (var day = 4; day < 20; day++)
        {
            book.CountLogin(Day(day));
        }

        Assert.True(book.WelcomeDays == RewardEvents.Welcome.Entries.Count, $"the days passed seven: {book.WelcomeDays}");

        var late = new RewardEventBook();
        late.CountLogin(Day(0));
        late.CountLogin(Day(RewardEvents.WelcomeCampaignDays));
        Assert.True(late.WelcomeDays == 1 && !late.WelcomeOpen(Day(RewardEvents.WelcomeCampaignDays)) && late.WelcomeOpen(Day(RewardEvents.WelcomeCampaignDays - 1)),
            "a day after the campaign counted, or the campaign's end is wrong");
        return Task.CompletedTask;
    }

    private static Task WelcomeClaimsAsync()
    {
        var source = new CatalogQuestItemSource(Items);
        var character = Classes.CreatePlayer(PlayerClass.Fighter, "Welcome");
        character.Events.CountLogin(Day(0));
        Assert.True(RewardClaims.Claim(character, source, RewardTrack.Welcome, 1, Day(0)).Status == RewardClaimStatus.NotReady
            && character.Events.State(RewardTrack.Welcome, 1, Day(0), 1) == RewardEntryState.Next, "day 2 was claimable after one day");

        var first = RewardClaims.Claim(character, source, RewardTrack.Welcome, 0, Day(0));
        Assert.True(first.Status == RewardClaimStatus.Ok && first.Claimed == 1 && QuestInventory.Count(character, ItemCodes.Parse("BI108")) == 3,
            $"day 1 gave {first.Status} and {QuestInventory.Count(character, ItemCodes.Parse("BI108"))} Teleport Cores");
        Assert.True(RewardClaims.Claim(character, source, RewardTrack.Welcome, 0, Day(1)).Status == RewardClaimStatus.AlreadyClaimed,
            "day 1 was claimed twice");

        character.Events.CountLogin(Day(1));
        var full = Classes.CreatePlayer(PlayerClass.Fighter, "Full");
        full.Events.CopyFrom(character.Events);
        foreach (var page in new[] { full.Inventory, full.HiddenBag })
        {
            while (page.TryAdd(new ItemInstance(Items.All[0])))
            {
            }
        }

        Assert.True(RewardClaims.Claim(full, source, RewardTrack.Welcome, 1, Day(1)).Status == RewardClaimStatus.NoSpace
            && full.Events.State(RewardTrack.Welcome, 1, Day(1), 1) == RewardEntryState.Ready, "a full bag lost the reward");
        Assert.True(RewardClaims.Claim(character, source, RewardTrack.Welcome, 1, Day(RewardEvents.WelcomeCampaignDays)).Status == RewardClaimStatus.Expired,
            "a day was claimed after the campaign");
        return Task.CompletedTask;
    }

    private static Task GoalsAsync()
    {
        var source = new CatalogQuestItemSource(Items);
        var character = Classes.CreatePlayer(PlayerClass.Fighter, "Hunter", RewardEvents.Daily.MinLevel - 1);
        var kills = RewardEvents.Daily.Entries[0];
        character.Events.Count(RewardGoal.MonsterKills, kills.Target, Day(0), character.Level);
        Assert.True(character.Events.Progress(RewardTrack.Daily, 0, Day(0), character.Level) == 0
            && RewardClaims.Claim(character, source, RewardTrack.Daily, 0, Day(0)).Status == RewardClaimStatus.LevelTooLow,
            "kills below the daily level counted");

        var daily = Classes.CreatePlayer(PlayerClass.Fighter, "Daily", RewardEvents.Daily.MinLevel);
        daily.Events.Count(RewardGoal.MonsterKills, kills.Target - 1, Day(0), daily.Level);
        Assert.True(daily.Events.State(RewardTrack.Daily, 0, Day(0), daily.Level) == RewardEntryState.Locked
            && daily.Events.Progress(RewardTrack.Weekly, 0, Day(0), daily.Level) == 0, "a goal short of its target was ready, or weekly counted below level 25");
        daily.Events.Count(RewardGoal.MonsterKills, 5, Day(0), daily.Level);
        var money = daily.Money;
        var paid = RewardClaims.Claim(daily, source, RewardTrack.Daily, RewardClaims.All, Day(0));
        Assert.True(paid.Status == RewardClaimStatus.Ok && paid.Claimed == 1 && paid.Gold == kills.Gold && daily.Money == money + kills.Gold
            && daily.Events.Progress(RewardTrack.Daily, 0, Day(0), daily.Level) == kills.Target, $"the daily claim paid {paid}");
        Assert.True(daily.Events.State(RewardTrack.Daily, 0, Day(1), daily.Level) == RewardEntryState.Locked
            && daily.Events.Progress(RewardTrack.Daily, 0, Day(0) with { SecondOfDay = 86_399 }, daily.Level) == kills.Target,
            "the daily goal did not reset at midnight, or reset before it");

        var weekly = Classes.CreatePlayer(PlayerClass.Fighter, "Weekly", RewardEvents.Weekly.MinLevel);
        var bellatra = RewardEvents.Weekly.Entries.Select((entry, index) => (entry, index)).Single(pair => pair.entry.Goal == RewardGoal.BellatraEntries);
        weekly.Events.Count(RewardGoal.BellatraEntries, bellatra.entry.Target, Day(0), weekly.Level);
        Assert.True(weekly.Events.State(RewardTrack.Weekly, bellatra.index, Day(6), weekly.Level) == RewardEntryState.Ready
            && weekly.Events.State(RewardTrack.Weekly, bellatra.index, Day(7), weekly.Level) == RewardEntryState.Locked,
            "the weekly goal did not last until Sunday or did not reset on Monday");
        weekly.Events.Count(RewardGoal.BellatraEntries, 1, Day(7), weekly.Level);
        Assert.True(weekly.Events.Progress(RewardTrack.Weekly, bellatra.index, Day(7), weekly.Level) == 1
            && weekly.Events.WeekStart == Day(7).WeekStart, "a new week did not start its count afresh");
        return Task.CompletedTask;
    }

    private static Task LevelRaceAsync()
    {
        var source = new CatalogQuestItemSource(Items);
        var first = RewardEvents.LevelRace.Entries[0].Target;
        var young = Classes.CreatePlayer(PlayerClass.Fighter, "Young", first - 1);
        Assert.True(RewardClaims.Claim(young, source, RewardTrack.LevelRace, 0, Day(0)).Status == RewardClaimStatus.NotReady, "a level not reached paid");
        var grown = Classes.CreatePlayer(PlayerClass.Fighter, "Grown", RewardEvents.LevelRace.Entries[1].Target);
        var all = RewardClaims.Claim(grown, source, RewardTrack.LevelRace, RewardClaims.All, Day(0));
        Assert.True(all.Status == RewardClaimStatus.Ok && all.Claimed == 0b11 && all.Gold == RewardEvents.LevelRace.Entries[1].Gold
            && RewardClaims.Claim(grown, source, RewardTrack.LevelRace, 1, Day(400)).Status == RewardClaimStatus.AlreadyClaimed,
            $"the level race paid {all} or paid twice");
        return Task.CompletedTask;
    }

    private static async Task WorldAsync()
    {
        var clock = new BellatraGameTests.GameClock { Now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero) };
        var fixture = new ChatFixture(clock: clock);
        var empty = fixture.Data.Current;
        fixture.Data.Current = new WorldData(Items, empty.Monsters, empty.Npcs, empty.FieldSpawns, empty.Motions);
        var rewards = new RewardEventAppService(fixture.World, new ImmediateSaves(fixture.Characters));
        var player = await fixture.JoinAsync("Newcomer", 0, 0);
        var character = player.Session.Character!;
        Assert.True(character.Events.WelcomeDays == 1, "entering the world counted no login day");

        fixture.Tick(1);
        RewardBoard? board = null;
        while (player.Outbox.Reader.TryRead(out var snapshot))
        {
            board = snapshot.Rewards ?? board;
        }

        Assert.True(board is not null && board.Tracks[0].Track == RewardTrack.Welcome
            && board.Tracks[0].Entries[0].State == RewardEntryState.Ready && board.Tracks[0].Entries[0].Items[0].Count == 3,
            "the snapshot carried no window with day 1 ready");

        clock.Now = clock.Now.AddDays(1);
        fixture.Tick(1);
        Assert.True(character.Events.WelcomeDays == 2, "a player playing across midnight counted no new day");

        foreach (var page in new[] { character.Inventory, character.HiddenBag })
        {
            while (page.TryAdd(new ItemInstance(Items.All[0])))
            {
            }
        }

        var refused = await fixture.RunAsync(rewards.ClaimAsync(player.Session, RewardTrack.Welcome, 0, CancellationToken.None));
        Assert.True(refused.Status == RewardClaimStatus.NoSpace && character.Events.State(RewardTrack.Welcome, 0, RewardDate.Of(clock.Now), 1) == RewardEntryState.Ready
            && RewardEvents.Message(refused.Status) == GameText.Of(GameTextId.NoSpace), "a full bag lost the reward or answered no line");

        QuestInventory.Remove(character, Items.All[0].Code);
        var claimed = await fixture.RunAsync(rewards.ClaimAsync(player.Session, RewardTrack.Welcome, RewardClaims.All, CancellationToken.None));
        var stored = fixture.Characters.Records["Newcomer"];
        Assert.True(claimed.Status == RewardClaimStatus.Ok && claimed.Claimed == 0b11 && character.Events.WelcomeClaimed == 0b11
            && RewardEventStateCodec.Read(stored.Events).WelcomeClaimed == 0b11, $"the claim of every ready day gave {claimed} and stored {stored.Events}");

        var view = await fixture.RunAsync(rewards.ViewAsync(player.Session));
        Assert.True(view is not null && view.Tracks.Count == RewardEvents.Tracks.Count
            && view.Tracks[0].Entries[2].State == RewardEntryState.Next, "the window asked for is wrong");
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-rewards-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "rewards.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var account = await provider.GetRequiredService<IAccountRepository>().CreateAsync("rewarded", "hash");
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(Classes, account.Id, "Rewarded", 0, PlayerClass.Fighter, 3));
            var live = Classes.CreatePlayer(PlayerClass.Fighter, "Rewarded", RewardEvents.Weekly.MinLevel);
            CharacterStateCodec.Restore(live, created, Items);
            live.Events.CountLogin(Day(0));
            live.Events.CountLogin(Day(2));
            live.Events.Count(RewardGoal.MonsterKills, 42, Day(2), live.Level);
            live.Events.MarkClaimed(RewardTrack.Welcome, 0, Day(2));
            live.Events.MarkClaimed(RewardTrack.LevelRace, 1, Day(2));
            var record = CharacterRecordFactory.FromLiveSession(created, live, 3, default) with { SaveSeq = 1 };
            Assert.True(await characters.UpdateAsync(record), "the capture was not stored");

            var loaded = await characters.FindByNameForAccountAsync(account.Id, "Rewarded");
            var restored = Classes.CreatePlayer(PlayerClass.Fighter, "Rewarded", RewardEvents.Weekly.MinLevel);
            CharacterStateCodec.Restore(restored, loaded!, Items);
            var (was, now) = (live.Events.Capture(), restored.Events.Capture());
            Assert.True(was with { Weekly = [], Daily = [] } == now with { Weekly = [], Daily = [] } && was.Weekly.SequenceEqual(now.Weekly)
                && was.Daily.SequenceEqual(now.Daily) && now.WelcomeDays == 2 && now.Weekly[0] == 42, $"the book came back as {now}");
            Assert.Throws<InvalidDataException>(() => CharacterStateCodec.Restore(Classes.CreatePlayer(PlayerClass.Fighter, "Rewarded"),
                loaded! with { Events = CharacterJson.Write(RewardEventStateCodec.Capture(live.Events) with { WelcomeClaimed = 1 << 9 }) }, Items),
                "a claim of an unknown day was taken");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var full = new RewardEventsPacket([.. Enumerable.Range(0, RewardWire.MaxTracks).Select(track => new RewardTrackWire((byte)track, 25,
            track == 3 ? null : 2_520_000, [.. Enumerable.Range(0, RewardWire.MaxEntries).Select(entry => new RewardEntryWire(2, 3, 9_999, 10_000, 1_000_000,
                [.. Enumerable.Range(0, RewardWire.MaxItems).Select(item => new RewardItemWire(ItemCodes.Parse("BI108"), 3, new string('é', 64)))]))]))]);
        var buffer = new byte[RewardEventsPacket.MaxSize];
        var written = full.Write(buffer);
        var read = RewardEventsPacket.Read(buffer.AsSpan(0, written));
        Assert.True(RewardEventsPacket.MaxSize <= FrameCodec.MaxFrameSize && written <= RewardEventsPacket.MaxSize && read.Tracks.Count == RewardWire.MaxTracks
            && read.Tracks[3].SecondsLeft is null && read.Tracks[0].SecondsLeft == 2_520_000 && read.Tracks[2].Entries[7].Items[2].Count == 3
            && read.Tracks[1].Entries[0] is { Goal: 2, State: 3, Progress: 9_999, Target: 10_000, Gold: 1_000_000 }, "the window did not round-trip");

        Assert.True(RewardEvents.Tracks.Count <= RewardWire.MaxTracks && RewardEvents.MaxEntries <= RewardWire.MaxEntries
            && RewardEvents.Tracks.All(track => track.Entries.Count <= RewardEvents.MaxEntries && track.Entries.All(entry => entry.Items.Count <= RewardWire.MaxItems)),
            "the reward tables outgrew the window's wire limits");

        var request = new RewardEventsRequestPacket(RewardEventsAction.ClaimAll, 2, 0);
        var result = new RewardEventsResultPacket(RewardEventsAction.Claim, 0, 4, 0, GameText.Of(GameTextId.NoSpace));
        var resultBuffer = new byte[RewardEventsResultPacket.MaxSize];
        var readResult = RewardEventsResultPacket.Read(resultBuffer.AsSpan(0, result.Write(resultBuffer)));
        Assert.True(RewardEventsRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request
            && readResult.Message == result.Message && readResult.Status == 4, "the request or the result did not round-trip");
        return Task.CompletedTask;
    }
}
