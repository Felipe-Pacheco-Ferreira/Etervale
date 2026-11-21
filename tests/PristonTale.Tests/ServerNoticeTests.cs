using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Settings;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Social;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

// The server's notices of other players' feats: a milestone level to everybody, a party member's loot to the party.
// The high aging is in NpcServiceTests, Bellatra's opening in BellatraGameTests, what the client hides in SettingsOptionsTests.
internal static class ServerNoticeTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("notice rules: agings from the limit, the highest milestone level passed, limits of 0 tell none, and the settings table reads them",
            RulesAsync);
        yield return ("a level that passes a milestone is told to every player, the highest of several gained at once; other levels are not", LevelAsync);
        yield return ("a monster's item a party member picks up is told to the other members, a unique or set item as rare; gold and players "
            + "outside the party are not", PartyLootAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(ServerNoticeRules.TellsAging(10, 10) && ServerNoticeRules.TellsAging(14, 10) && !ServerNoticeRules.TellsAging(9, 10)
            && !ServerNoticeRules.TellsAging(20, 0), "the aging limit");
        Assert.True(ServerNoticeRules.LevelMilestone(99, 100, 100, 10) == 100 && ServerNoticeRules.LevelMilestone(100, 101, 100, 10) == 0
            && ServerNoticeRules.LevelMilestone(89, 90, 100, 10) == 0 && ServerNoticeRules.LevelMilestone(105, 122, 100, 10) == 120
            && ServerNoticeRules.LevelMilestone(99, 100, 0, 10) == 0 && ServerNoticeRules.LevelMilestone(100, 100, 100, 10) == 0,
            "the milestone levels");
        Assert.True(new ItemDefinition(1, "Set", ItemKind.Ring) { SetCode = 3 }.IsRare && new ItemDefinition(1, "Unique", ItemKind.Axe) { UniqueItem = 1 }.IsRare
            && !new ItemDefinition(1, "Plain", ItemKind.Axe).IsRare, "the rare items");

        var problems = new List<string>();
        var settings = ServerSettingCatalog.Read(new Dictionary<string, string>
        {
            ["notices.aging_from"] = "12", ["notices.level_from"] = "0", ["notices.level_every"] = "0"
        }, null, problems);
        Assert.True(settings.World.Notices == new ServerNoticeOptions(12, 0, ServerNoticeRules.DefaultLevelEvery) && problems is [var refused]
            && refused.StartsWith("notices.level_every", StringComparison.Ordinal), "the settings table did not read the notice limits");
        Assert.Throws<InvalidOperationException>(() => (WorldServerOptions.Default with { Notices = new ServerNoticeOptions(21) }).Validated(),
            "an aging notice past the last level was accepted");
        return Task.CompletedTask;
    }

    // The lines the world's notices brought to the player since the last call.
    private static GameText[] Heard(ChatFixture fixture, ChatFixture.Player player)
    {
        fixture.Directory.Deliver(fixture.World.TakeTickNotices());
        lock (player.Link.Lines)
        {
            var heard = player.Link.Lines.Where(static line => !line.Line.IsNone).Select(static line => line.Line).ToArray();
            player.Link.Lines.Clear();
            return heard;
        }
    }

    private static async Task LevelAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0, level: 98);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        fixture.Tick(1);
        Heard(fixture, bob);

        ann.Session.Character!.LevelUp();
        fixture.Tick(1);
        Assert.True(Heard(fixture, bob).Length == 0, "level 99 was told");

        ann.Session.Character.LevelUp();
        fixture.Tick(1);
        var heard = Heard(fixture, bob);
        Assert.True(heard is [var told] && told == GameText.Of(GameTextId.LevelNotice, "Ann", "100"),
            "level 100 was not told once to everybody: " + string.Join(", ", heard));

        for (var level = 100; level < 122; level++)
        {
            ann.Session.Character.LevelUp();
        }

        fixture.Tick(1);
        heard = Heard(fixture, bob);
        Assert.True(heard is [var highest] && highest == GameText.Of(GameTextId.LevelNotice, "Ann", "120"),
            "several levels at once did not tell the highest milestone once: " + string.Join(", ", heard));
    }

    private static async Task PartyLootAsync()
    {
        var fixture = new ChatFixture();
        var parties = new PartyAppService(fixture.Sessions, fixture.World, fixture.Directory);
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 100, 0);
        var cid = await fixture.JoinAsync("Cid", 200, 0);
        await fixture.RunAsync(parties.InviteAsync(ann.Session.SessionToken, bob.Id));
        await fixture.RunAsync(parties.AnswerAsync(bob.Session.SessionToken, ann.Id, true));
        var sessions = new SessionAppService(fixture.Sessions, new CharacterClassRegistry(), fixture.Characters, new ImmediateSaves(fixture.Characters),
            fixture.Catalog, fixture.World, fixture.Data);
        var field = fixture.World.FieldFor(ChatFixture.Field)!;

        async Task PickUp(RolledDrop drop)
        {
            field.GroundLoot.Place(new KillPayout(1, ann.Id, 0, [drop]), Vector3.Zero, true, field.NowMs, new SeededSimRng(1));
            fixture.Tick(1);
            var lying = field.GroundLoot.Snapshot().Single().View;
            Assert.True(await fixture.RunAsync(fixture.World.ReportMovementAsync(ChatFixture.Field, ann.Id, lying.Position, 0f, true, true, 0))
                == MovementVerdict.Accepted, "the walk to the drop was refused");
            var command = new InventoryCommand(Guid.NewGuid(), ann.Session.Character!.InventoryRevision, InventoryAction.Pickup, lying.Id, 0, 1, default);
            Assert.True((await fixture.RunAsync(sessions.ApplyInventoryAsync(ann.Session.SessionToken, command))).Ok, "pickup refused");
        }

        Heard(fixture, bob);
        Heard(fixture, cid);
        Heard(fixture, ann);
        await PickUp(RolledDrop.Item(ChatFixture.Axe.Code));
        var heard = Heard(fixture, bob);
        Assert.True(heard is [var loot] && loot == GameText.Of(GameTextId.PartyLoot, "Ann", ChatFixture.Axe.Name), "the member was not told the loot: "
            + string.Join(", ", heard));
        Assert.True(Heard(fixture, cid).Length == 0 && Heard(fixture, ann).Length == 0, "the picker or a player outside the party was told the loot");

        await PickUp(RolledDrop.Gold(500));
        Assert.True(Heard(fixture, bob).Length == 0, "picked gold was told as loot");
    }
}
