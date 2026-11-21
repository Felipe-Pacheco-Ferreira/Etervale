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
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Royale;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Calendar;

namespace PristonTale.Tests;

// The Battle Royale (RoyaleRules): the rule, the stand-in and its guard against storage, registration and its refusals,
// a cancelled event, a full event from the start to the places with fights, the zone and the return, saves during the
// event, the potions, the prizes in a disposable database, and the wire, settings, calendar and chat filter.
internal static class RoyaleTests
{
    private const int Fighting = 85;
    private static readonly DateTimeOffset Registration = new(2026, 11, 20, 16, 50, 0, TimeSpan.Zero);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("battle royale rules: the zone's steps, registration and start minutes, the places, potions, experience and the ranking", RulesAsync);
        yield return ("a battle royale stand-in is the class at level 100 with the table's attributes, the kit and the own skills, and is never stored",
            StandInAsync);
        yield return ("battle royale registration refuses a closed window, a low level and a busy player, and fewer than ten cancel the event", RegisterAsync);
        yield return ("a battle royale runs from the start to the places: stand-ins fight and are spared, the zone takes players out, saves keep the "
            + "own character where it registered, and everyone goes back with experience", EventAsync);
        yield return ("battle royale prizes are mailed once per event", StorageAsync);
        yield return ("the battle royale reaches the wire, the settings, the calendar and the chat filter", WireAsync);
    }

    private sealed class UtcClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Registration;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static Task RulesAsync()
    {
        Assert.True(RoyaleRules.Radius(0) == RoyaleRules.FirstRadius && RoyaleRules.Radius(RoyaleRules.ZoneSteps) == RoyaleRules.LastRadius
            && RoyaleRules.Radius(99) == RoyaleRules.LastRadius && RoyaleRules.StepsAfter(RoyaleRules.ZoneStepMs * 2.5f) == 2, "the zone's steps differ");
        Assert.True(RoyaleRules.InZone(RoyaleRules.Center + new Vector3(100, 50, 0), 150) && !RoyaleRules.InZone(RoyaleRules.Center + new Vector3(200, 0, 0), 150),
            "the zone's edge differs");
        const int mask = 1 << 17;
        Assert.True(RoyaleRules.RegistrationOpen(mask, Registration) && !RoyaleRules.RegistrationOpen(mask, Registration.AddMinutes(-1))
            && !RoyaleRules.RegistrationOpen(mask, Registration.AddMinutes(10)) && RoyaleRules.Starts(mask, Registration.AddMinutes(10))
            && !RoyaleRules.Starts(mask, Registration.AddMinutes(11)) && RoyaleRules.RegistrationOpen(1, new DateTimeOffset(2026, 11, 20, 23, 55, 0, TimeSpan.Zero)),
            "registration and the start differ");
        Assert.True(Vector3.Distance(RoyaleRules.Place(3, 10), RoyaleRules.Center) is > 699f and < 701f
            && Vector3.Distance(RoyaleRules.Place(0, 10), RoyaleRules.Place(5, 10)) > 1399f, "the start places differ");
        Assert.True(RoyaleRules.InZone(RoyaleRules.PointInZone(400, 0.3f, 0.99f), 400) && RoyaleRules.PotionsFor(0) == 0 && RoyaleRules.PotionsFor(3) == 1
            && RoyaleRules.PotionsFor(12) == 2 && RoyaleRules.ZoneDamage(1000) == 50 && RoyaleRules.ZoneDamage(10) == 1, "the potions or the zone's damage differ");
        var next = Simulation.Progression.ExperienceTable.ForLevel(91);
        var start = Simulation.Progression.ExperienceTable.ForLevel(90);
        Assert.True(RoyaleRules.Experience(90, start) == (next - start) * 2 / 100 && RoyaleRules.Experience(90, next) == 0
            && RoyaleRules.Experience(Simulation.Progression.ExperienceTable.MaximumLevel, 0) == 0, "the experience differs");
        Assert.True(RoyaleRules.EventKey(Registration.AddMinutes(10)) == 2026112017 && RoyaleRules.Over(1, 0) && RoyaleRules.Over(5, RoyaleRules.LimitMs)
            && !RoyaleRules.Over(2, 1000), "the key or the end differs");

        var royale = new RoyaleEvent(1, 1);
        var origin = new RoyaleEntry(3, Vector3.Zero);
        for (var index = 0; index < 4; index++)
        {
            royale.Seats.Add(new RoyaleSeat(royale, index, (ulong)index + 1, "P" + index, index, origin));
        }

        royale.Seats[0].Damage = 10;
        royale.Seats[1].Damage = 30;
        royale.TakeOut(royale.Seats[2]);
        royale.TakeOut(royale.Seats[3]);
        royale.TakeOut(royale.Seats[3]);
        Assert.True(royale.Standing == 2 && royale.Ranking() is [{ Name: "P1" }, { Name: "P0" }, { Name: "P3" }, { Name: "P2" }],
            "the ranking is not the standing by damage, then the last out first");
        return Task.CompletedTask;
    }

    private static Task StandInAsync()
    {
        var classes = new CharacterClassRegistry();
        var player = classes.CreatePlayer(Contracts.Characters.PlayerClass.Fighter, "Ann", Fighting);
        player.Skills.Restore(player.Class.Skills.First().Code, 3, 7);
        var items = new Simulation.Items.ItemCatalog([ChatFixture.Axe, ChatFixture.Potion]);
        var standIn = RoyaleKits.StandIn(player, RoyaleKits.Kit(player.Class, items));
        var attributes = RoyaleKits.Attributes(player.Definition);
        Assert.True(standIn is { IsStandIn: true, Level: RoyaleRules.KitLevel } && standIn.Attributes == attributes
            && attributes.Total == player.Definition.BaseAttributes.Total + (RoyaleRules.KitLevel - 1) * PlayerCharacter.StatusPointsPerLevel
            && attributes.Strength == 208, "the stand-in is not the class at the kit's level with the table's attributes");
        Assert.True(standIn.GetEquipped(EquipmentSlot.Weapon)?.Definition == ChatFixture.Axe && standIn.Life == standIn.MaxLife
            && standIn.Skills.Ranks.Count == player.Skills.Ranks.Count && player.GetEquipped(EquipmentSlot.Weapon) is null,
            "the stand-in does not wear the kit or keep the skills, or the player was changed");
        Assert.Throws<InvalidOperationException>(() => standIn.CloneForInventoryOperation(), "a character change started from a stand-in");
        var record = CharacterRecordFactory.NewStarter(classes, 1, "Ann", 0, Contracts.Characters.PlayerClass.Fighter, 3);
        Assert.Throws<InvalidOperationException>(() => CharacterRecordFactory.Capture(record, standIn, 3, Vector3.Zero), "a stand-in was stored");
        return Task.CompletedTask;
    }

    private static async Task<(ChatFixture Fixture, UtcClock Clock)> OpenAsync()
    {
        var clock = new UtcClock();
        var fixture = new ChatFixture(clock: clock);
        await fixture.RunAsync(fixture.World.ApplyOptionsAsync(fixture.World.Options with { Royale = fixture.World.Options.Royale with { Hours = 1 << 17 } }));
        fixture.Tick(1);
        return (fixture, clock);
    }

    private static async Task<List<ChatFixture.Player>> RegisteredAsync(ChatFixture fixture, int count)
    {
        var players = new List<ChatFixture.Player>();
        for (var index = 0; index < count; index++)
        {
            var player = await fixture.JoinAsync("Royal" + index, 10 + index * 30, 10, level: Fighting);
            await fixture.SayAsync(player, "/royale");
            players.Add(player);
        }

        return players;
    }

    private static async Task RegisterAsync()
    {
        var clock = new UtcClock { Now = Registration.AddMinutes(-5) };
        var fixture = new ChatFixture(clock: clock);
        await fixture.RunAsync(fixture.World.ApplyOptionsAsync(fixture.World.Options with { Royale = fixture.World.Options.Royale with { Hours = 1 << 17 } }));
        var early = await fixture.JoinAsync("Early", 0, 0, level: Fighting);
        fixture.Tick(1);
        Assert.True(await fixture.RunAsync(fixture.World.JoinRoyaleAsync(early.Session, true)) == RoyaleJoin.Closed, "a closed registration took a player");

        clock.Now = Registration;
        fixture.Tick(1);
        Assert.True(fixture.World.TakeTickNotices().Any(static notice => notice.Text.Id == GameTextId.RoyaleOpen), "the registration's opening was not told");
        var rookie = await fixture.JoinAsync("Rookie", 0, 0, level: 60);
        Assert.True(await fixture.RunAsync(fixture.World.JoinRoyaleAsync(rookie.Session, true)) == RoyaleJoin.Level, "a low level registered");
        await fixture.SayAsync(early, "/royale");
        Assert.True(Body(fixture, early).RoyaleEntry is { FieldId: ChatFixture.Field }, "the registration was not kept");
        await fixture.SayAsync(early, "/royale sair");
        Assert.True(Body(fixture, early).RoyaleEntry is null
            && await fixture.RunAsync(fixture.World.JoinRoyaleAsync(early.Session, false)) == RoyaleJoin.NotRegistered, "the registration was not left");

        var few = await RegisteredAsync(fixture, RoyaleRules.MinimumPlayers - 1);
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(few[0])!.Value.Royale is { Registered: true, Total: RoyaleRules.MinimumPlayers - 1 }, "the registration did not reach the snapshot");
        fixture.World.TakeTickNotices();
        clock.Now = Registration.AddMinutes(10);
        fixture.Tick(1);
        Assert.True(fixture.World.RunningRoyale is null && few.All(player => Body(fixture, player).RoyaleEntry is null)
            && fixture.World.TakeTickNotices().Any(static notice => notice.Text == GameText.Of(GameTextId.RoyaleCancelled, "9", "10")),
            "too few players did not cancel the event");
    }

    private static async Task EventAsync()
    {
        var (fixture, clock) = await OpenAsync();
        var players = await RegisteredAsync(fixture, RoyaleRules.MinimumPlayers);
        var homes = players.Select(player => Body(fixture, player).Character).ToList();
        var experience = homes.Select(static home => home.Experience).ToList();
        var busy = await fixture.JoinAsync("Busy", 500, 500, level: Fighting);
        await fixture.SayAsync(busy, "/royale");
        Body(fixture, busy).Hidden = true;
        var results = new List<RoyaleResult>();
        fixture.World.RoyaleResultRecorded = results.Add;
        clock.Now = Registration.AddMinutes(10);
        fixture.Tick(1);

        var bodies = players.Select(player => RoyaleBody(fixture, player)).ToList();
        Assert.True(fixture.World.RunningRoyale is { Seats.Count: RoyaleRules.MinimumPlayers } && Body(fixture, busy) is { Royale: null, RoyaleEntry: null },
            "the registered players did not start, or a busy one did");
        Assert.True(bodies.All(static body => body.Character is { IsStandIn: true, Level: RoyaleRules.KitLevel } && body.Home is not null)
            && players.Select(static player => player.Session.Character).SequenceEqual(homes), "the players do not fight as stand-ins with their own characters kept");
        var save = await fixture.RunAsync(fixture.World.CaptureForSaveAsync(players[0].Session));
        Assert.True(save is { Scalars: { FieldId: ChatFixture.Field, Level: Fighting, PosX: 10f } }, "a save during the event stored the stand-in or the arena");

        // The players start apart and nobody walks here: the attacker stands by its foe.
        var arena = ArenaField(fixture);
        bodies[0].Teleport(bodies[1].Position + new Vector3(20, 0, 0));
        bodies[1].ApplyDamage(bodies[1].Life - 2);
        Assert.True(arena.SetAttackTarget(players[0].Id, players[1].Id, sustained: true), "a stand-in could not fight another");
        for (var step = 0; step < 300 && bodies[1].Royale is { Out: false }; step++)
        {
            fixture.Tick(20);
        }

        // Regeneration may have given a point back since the spared blow.
        Assert.True(bodies[1] is { Royale.Out: true, IsDead: false, Life: > 0 and < 10 } && !arena.SetAttackTarget(players[0].Id, players[1].Id, sustained: false)
            && !arena.SetAttackTarget(players[1].Id, players[2].Id, sustained: false),
            "the blow did not spare and take out, or the one out still fights");
        Assert.True(WorldServer.DrinkRoyalePotion(bodies[2], Simulation.Items.ItemCodes.Parse("PL104")) && !WorldServer.DrinkRoyalePotion(bodies[1],
            Simulation.Items.ItemCodes.Parse("PL104")) && !WorldServer.DrinkRoyalePotion(bodies[2], ChatFixture.Potion.Code), "the potions are not drunk only by those standing");

        Assert.True(!bodies[2].MaySupport(bodies[3]) && bodies[2].MaySupport(bodies[2]) && !Body(fixture, busy).MaySupport(bodies[2])
            && ArenaField(fixture).EventDrops is not null, "a player supports another in the event, or the event's potions are not the field's");

        // One leaves the world mid-event: it is out, and its next entry starts where it registered.
        var leaver = players[^1];
        await fixture.RunAsync(fixture.World.LeaveAsync(RoyaleRules.FieldCode, leaver.Id, leaver.Outbox.Writer));
        fixture.Tick(1);
        Assert.True(leaver.Session.FieldId == ChatFixture.Field && fixture.World.RunningRoyale!.Seats[^1].Out, "the leaver was not taken out or kept in the arena");

        var notices = new List<WorldNotice>();
        for (var index = 2; index < players.Count - 1; index++)
        {
            bodies[index].ApplyDamage(bodies[index].Life - 2);
            bodies[index].Teleport(RoyaleRules.Center + new Vector3(RoyaleRules.FirstRadius + 300, 0, 0));
        }

        fixture.Tick((int)(RoyaleRules.ZoneDamageEveryMs / 50f) + 2);
        notices.AddRange(fixture.World.TakeTickNotices());
        Assert.True(fixture.World.RunningRoyale is null && ArenaField(fixture).EventDrops is null
            && results is [{ Places: [{ Place: 1, Name: "Royal0" }, ..] places }] && places.Count == players.Count,
            "the last one standing did not win");
        for (var index = 0; index < players.Count - 1; index++)
        {
            var body = Body(fixture, players[index]);
            Assert.True(ReferenceEquals(body.Character, homes[index]) && body.Royale is null && body.Life == body.MaxLife
                && Vector2.Distance(new Vector2(body.Position.X, body.Position.Z), new Vector2(10 + index * 30, 10)) < 1f
                && body.Character.Experience > experience[index], $"{players[index].Session.CharacterName} did not go back with its character and experience");
        }

        Assert.True(notices.Any(notice => notice.Text == GameText.Of(GameTextId.RoyaleWinner, "Royal0"))
            && notices.Any(notice => notice.To == players[0].Id && notice.Text.Id == GameTextId.RoyalePlace), "the places and the winner were not told");
        Assert.True(fixture.World.HolderOf(Simulation.Titles.TitleCode.Survivor) is { Name: "Royal0" }, "the winner did not take the survivor's title");
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-royale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var royale = provider.GetRequiredService<IRoyaleRepository>();
            var accounts = provider.GetRequiredService<Persistence.Abstractions.Accounts.IAccountRepository>();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var classes = new CharacterClassRegistry();
            var ids = new List<long>();
            foreach (var name in new[] { "Ann", "Bob" })
            {
                var account = await accounts.CreateAsync(name.ToLowerInvariant(), "hash");
                ids.Add((await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, name, 0, Contracts.Characters.PlayerClass.Knight, 3))).Id);
            }

            const long key = 2026112017;
            RoyalePrize[] prizes = [new(1, ids[0], 1000), new(2, ids[1], 0), new(3, 999_999, 300)];
            Assert.True(await royale.AwardAsync(key, prizes, "Prêmio", "Parabéns", DateTime.UtcNow) is [{ Place: 1 }, { Place: 2 }]
                && (await royale.AwardAsync(key, prizes, "Prêmio", "Parabéns", DateTime.UtcNow)).Count == 0
                && (await royale.AwardAsync(key + 1, [], "x", "y", DateTime.UtcNow)).Count == 0
                && (await royale.AwardAsync(key + 1, prizes, "x", "y", DateTime.UtcNow)).Count == 0, "an event was paid twice, or a missing character was paid");
            await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "world.db")}");
            await connection.OpenAsync();
            var mails = connection.CreateCommand();
            mails.CommandText = "SELECT COUNT(*) FROM game_mail WHERE gold = 1000 AND recipient_account_id = sender_account_id";
            Assert.True((long)(await mails.ExecuteScalarAsync())! == 1, "the prize was not mailed, or a prize of no gold was");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var rules = new EventScheduleRules { RoyaleHours = 1 << 16 };
        var now = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var royale = EventSchedule.Occurrences(rules, now, TimeZoneInfo.Utc, TimeSpan.FromDays(1)).Where(static o => o.Kind == CalendarEventKind.Royale).ToList();
        Assert.True(royale is [{ } first, ..] && first.Start == now.AddHours(16) && first.End == now.AddHours(16).AddMinutes(RoyaleRules.LimitMinutes),
            "the battle royale is not in the calendar");

        var problems = new List<string>();
        var read = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["royale.hours"] = "", ["royale.prize_first"] = "7" }, null, problems);
        Assert.True(read.World.Royale is { Hours: 0, FirstPrize: 7 } && problems.Count == 0, "the battle royale settings were not read");
        var world = WorldServerOptions.Default;
        Assert.Throws<InvalidOperationException>(() => (world with { Royale = world.Royale with { Hours = world.Arena.Hours } }).Validated(),
            "the battle royale took an arena hour");

        var potion = ChatFixture.Potion with { Code = RoyaleRules.LifePotion, CatalogKey = "PL104" };
        var ground = new FieldGroundLoot(new Simulation.Items.ItemCatalog([potion]));
        var laid = ground.PlaceEventItem(RoyaleRules.LifePotion, Vector3.Zero, 0, new Simulation.Combat.CombatRng(1));
        var count = ground.Count;
        ground.Withdraw(laid);
        Assert.True(laid != Guid.Empty && count == 1 && ground.Count == 0 && ground.PlaceEventItem(RoyaleRules.ManaPotion, Vector3.Zero, 0, new CombatRng(1)) == Guid.Empty,
            "an event's potion was not laid and taken away, or one the catalog lacks was");

        var buffer = new byte[RoyaleStatePacket.Size];
        var sent = new RoyaleStatePacket(RoyaleStatePacket.FightingState, 7, 30, 660, 41, 512);
        Assert.True(RoyaleStatePacket.Read(buffer.AsSpan(0, sent.Write(buffer))) == sent, "the battle royale state did not round-trip");
        buffer[12] = 9;
        Assert.Throws<InvalidDataException>(() => RoyaleStatePacket.Read(buffer), "an unknown battle royale state was read");
        Assert.True(new RoyaleInfo(RoyaleStatePacket.OutState, 1, 2, 3, 4, 5) is { Active: true, Out: true, Fighting: false }
            && !new RoyaleInfo(RoyaleStatePacket.NoneState, 0, 0, 0, 0, 0).Active, "the battle royale state is not read by the client");

        var notice = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.RoyaleWinner, "Ann"));
        var own = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.RoyalePlace, "1", "50"));
        var hiding = new ClientSettings { HideRoyaleNotices = true };
        Assert.True(ChatLineFilter.Hides(hiding, notice) && !ChatLineFilter.Hides(hiding, own) && !ChatLineFilter.Hides(new ClientSettings(), notice),
            "the battle royale notices were not hidden by the option alone");
        return Task.CompletedTask;
    }

    private static FieldInstance Field(ChatFixture fixture) =>
        fixture.World.TryGetOpenField(ChatFixture.Field, out var field) ? field : throw new InvalidOperationException("the fixture field is not open");

    private static FieldInstance ArenaField(ChatFixture fixture) =>
        fixture.World.TryGetOpenField(RoyaleRules.FieldCode, out var field) ? field : throw new InvalidOperationException("the arena field is not open");

    private static WorldPlayer Body(ChatFixture fixture, ChatFixture.Player player) =>
        ((IFieldCombatHost)Field(fixture)).TryGetPlayer(player.Id, out var body) ? body : throw new InvalidOperationException("the player is not in the field");

    private static WorldPlayer RoyaleBody(ChatFixture fixture, ChatFixture.Player player) =>
        ((IFieldCombatHost)ArenaField(fixture)).TryGetPlayer(player.Id, out var body) ? body : throw new InvalidOperationException("the player is not in the event");
}
