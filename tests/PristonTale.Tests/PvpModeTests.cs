using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

// The PvP mode (PvpRules): the rule itself, the server's switch and answers, who fights whom in a hunting field, the
// murderer's mark, a free death, the wire and the settings.
internal static class PvpModeTests
{
    private const int Fighting = 60;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("PvP rules: hunting fields only, level 50 to turn on, the hold and the mark to turn off, mates spared but a murderer, the damage percent",
            RulesAsync);
        yield return ("the PvP switch answers the player and reaches every viewer's looks; two players with PvP on fight in a hunting field, never in a town",
            SwitchAsync);
        yield return ("party and clan mates never fight in PvP but a murderer is fought by its own; a blow holds the switch on for a minute", MatesAndHoldAsync);
        yield return ("a PvP kill marks the killer a murderer and tells it, a murderer's killer stays clean, and the slain loses nothing", KillAsync);
        yield return ("the PvP switch and flags cross the wire, pvp.damage_percent reaches the fields, and K is the PvP shortcut", WireAndSettingsAsync);
        yield return ("a castle blow touches neither the PvP hold, the mark nor the death; leaving keeps a murderer's mark on the real clock", CastleAndLeaveAsync);
    }

    private static async Task CastleAndLeaveAsync()
    {
        var castle = new ChatFixture(state: FieldState.Castle);
        var ann = await castle.JoinAsync("Ann", 0, 0, level: Fighting);
        var bob = await castle.JoinAsync("Bob", 20, 20, level: Fighting);
        var (annBody, bobBody) = (Body(castle, ann), Body(castle, bob));
        bobBody.Kill();
        Field(castle).RecordPvpBlow(annBody, bobBody, 10);
        Assert.True(!bobBody.SlainByPlayer && annBody.PvpCombatMsLeft == 0f && !annBody.Murderer,
            "a castle kill went through the PvP mode");
        Assert.Throws<InvalidOperationException>(() => (WorldServerOptions.Default with { PvpDamagePercent = 0 }).Validated(), "a damage percent of 0 was accepted");

        var clock = new SpecialItemTests.ManualClock();
        var fixture = new ChatFixture(clock: clock);
        var cid = await fixture.JoinAsync("Cid", 0, 0, level: Fighting);
        var body = Body(fixture, cid);
        await fixture.RunAsync(fixture.World.SwitchPvpAsync(cid.Id, true));
        body.MarkMurderer();
        await fixture.RunAsync(fixture.World.LeaveAsync(ChatFixture.Field, cid.Id, cid.Outbox.Writer));
        clock.Now += TimeSpan.FromMinutes(4);
        var back = Body(fixture, await fixture.JoinAsync("Cid", 0, 0, level: Fighting));
        Assert.True(back.Pvp && back.Murderer && Math.Abs(back.MurdererMsLeft - (PvpRules.MurdererMs - 240_000f)) < 1000f,
            "leaving cleared the murderer's mark or stopped its clock");
        Assert.True(await fixture.RunAsync(fixture.World.SwitchPvpAsync(back.EntityId, false)) == PvpSwitch.Murderer, "the mark let PvP turn off after a leave");
    }

    private static Task RulesAsync()
    {
        Assert.True(PvpRules.FieldAllows(FieldState.Forest) && PvpRules.FieldAllows(FieldState.Dungeon) && PvpRules.FieldAllows(FieldState.Ice)
            && !PvpRules.FieldAllows(FieldState.Village) && !PvpRules.FieldAllows(FieldState.Castle) && !PvpRules.FieldAllows(FieldState.Room)
            && !PvpRules.FieldAllows(FieldState.QuestArena) && !PvpRules.FieldAllows(FieldState.Action), "the hunting fields differ");
        Assert.True(PvpRules.Switch(true, PvpRules.MinimumLevel - 1, 0f, 0f) == PvpSwitch.LevelTooLow
            && PvpRules.Switch(true, PvpRules.MinimumLevel, 5000f, 5000f) == PvpSwitch.On, "turning on does not follow the level alone");
        Assert.True(PvpRules.Switch(false, Fighting, 1f, 0f) == PvpSwitch.InCombat && PvpRules.Switch(false, Fighting, 1f, 1f) == PvpSwitch.Murderer
            && PvpRules.Switch(false, Fighting, 0f, 0f) == PvpSwitch.Off, "turning off does not wait for the hold and the mark");
        Assert.True(PvpRules.MayFight(FieldState.Forest, true, true, false, false, false) && !PvpRules.MayFight(FieldState.Forest, true, false, false, false, false)
            && !PvpRules.MayFight(FieldState.Forest, false, true, true, false, false) && !PvpRules.MayFight(FieldState.Village, true, true, true, false, false),
            "two players fight without both switches on, or in a town");
        Assert.True(!PvpRules.MayFight(FieldState.Forest, true, true, false, true, false) && !PvpRules.MayFight(FieldState.Forest, true, true, false, false, true)
            && PvpRules.MayFight(FieldState.Forest, true, true, true, true, true), "mates fought, or a murderer was spared by its own");
        Assert.True(PvpRules.SameClan(9, 9) && !PvpRules.SameClan(0, 0) && !PvpRules.SameClan(9, 7), "two players without a clan share one");
        Assert.True(PvpRules.Damage(100, 50) == 50 && PvpRules.Damage(1, 50) == 1 && PvpRules.Damage(0, 50) == 0 && PvpRules.Damage(int.MaxValue, 1000) > 0,
            "the damage percent differs");
        Assert.True(PvpRules.MakesMurderer(false) && !PvpRules.MakesMurderer(true) && PvpRules.SecondsLeft(59_001f) == 60 && PvpRules.SecondsLeft(-1f) == 0,
            "the mark or the seconds left differ");
        return Task.CompletedTask;
    }

    private static async Task SwitchAsync()
    {
        var fixture = new ChatFixture();
        var rookie = await fixture.JoinAsync("Rookie", 0, 0);
        var ann = await fixture.JoinAsync("Ann", 0, 0, level: Fighting);
        var bob = await fixture.JoinAsync("Bob", 20, 20, level: Fighting);
        var field = Field(fixture);
        fixture.World.TakeTickNotices();

        Assert.True(await fixture.RunAsync(fixture.World.SwitchPvpAsync(rookie.Id, true)) == PvpSwitch.LevelTooLow && !Body(fixture, rookie).Pvp
            && Told(fixture, rookie, GameTextId.PvpLevel) is [var level] && level == PvpRules.MinimumLevel.ToString(), "a low level turned PvP on");
        Assert.True(await fixture.RunAsync(fixture.World.SwitchPvpAsync(ann.Id, true)) == PvpSwitch.On && Told(fixture, ann, GameTextId.PvpOn) is not null,
            "a fighter could not turn PvP on");
        Assert.True(!field.SetAttackTarget(ann.Id, bob.Id, sustained: false), "a player with PvP off was a target");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(bob)!.Value.Entities.Single(entity => entity.EntityId == ann.Id).Appearance.Pvp, "the viewer did not see the PvP mode");

        Assert.True(await fixture.RunAsync(fixture.World.SwitchPvpAsync(bob.Id, true)) == PvpSwitch.On, "the second player could not turn PvP on");
        Assert.True(field.SetAttackTarget(ann.Id, bob.Id, sustained: false), "two players with PvP on in a hunting field did not fight");

        var town = new ChatFixture(state: FieldState.Village);
        var cid = await town.JoinAsync("Cid", 0, 0, level: Fighting);
        var dan = await town.JoinAsync("Dan", 20, 20, level: Fighting);
        Assert.True(await town.RunAsync(town.World.SwitchPvpAsync(cid.Id, true)) == PvpSwitch.On
            && await town.RunAsync(town.World.SwitchPvpAsync(dan.Id, true)) == PvpSwitch.On, "PvP could not be switched in a town");
        Assert.True(!Field(town).SetAttackTarget(cid.Id, dan.Id, sustained: false), "players fought in a town");
    }

    private static async Task MatesAndHoldAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0, level: Fighting);
        var bob = await fixture.JoinAsync("Bob", 20, 20, level: Fighting);
        var field = Field(fixture);
        var (annBody, bobBody) = (Body(fixture, ann), Body(fixture, bob));
        await fixture.RunAsync(fixture.World.SwitchPvpAsync(ann.Id, true));
        await fixture.RunAsync(fixture.World.SwitchPvpAsync(bob.Id, true));

        annBody.Clan = bobBody.Clan = new ClanTag(9, "Tigers", 0, Registered: true);
        Assert.True(!field.SetAttackTarget(ann.Id, bob.Id, sustained: false), "a clan mate was a target");
        bobBody.MarkMurderer();
        Assert.True(field.SetAttackTarget(ann.Id, bob.Id, sustained: false), "a murderer was spared by its clan");
        annBody.Clan = null;
        var party = new Party(ann.Id, bob.Id);
        annBody.Party = bobBody.Party = party;
        Assert.True(field.SetAttackTarget(ann.Id, bob.Id, sustained: false), "a murderer was spared by its party");
        Assert.True(!field.SetAttackTarget(bob.Id, ann.Id, sustained: false), "a party mate was a target");
        annBody.Party = bobBody.Party = null;

        fixture.World.TakeTickNotices();
        Assert.True(annBody.PvpCombatMsLeft > 0f && await fixture.RunAsync(fixture.World.SwitchPvpAsync(ann.Id, false)) == PvpSwitch.InCombat && annBody.Pvp
            && Told(fixture, ann, GameTextId.PvpInCombat) is [var seconds] && int.Parse(seconds) is > 0 and <= 60, "the attacker turned PvP off at once");
        Assert.True(await fixture.RunAsync(fixture.World.SwitchPvpAsync(bob.Id, false)) == PvpSwitch.Murderer && bobBody.Pvp, "a murderer turned PvP off");
        fixture.Tick((int)(PvpRules.CombatHoldMs / 50f) + 2);
        Assert.True(await fixture.RunAsync(fixture.World.SwitchPvpAsync(ann.Id, false)) == PvpSwitch.Off && !annBody.Pvp
            && Told(fixture, ann, GameTextId.PvpOff) is not null, "the switch did not turn off after the hold");
    }

    private static async Task KillAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0, level: Fighting);
        var bob = await fixture.JoinAsync("Bob", 20, 20, level: Fighting);
        var field = Field(fixture);
        var (annBody, bobBody) = (Body(fixture, ann), Body(fixture, bob));
        await fixture.RunAsync(fixture.World.SwitchPvpAsync(ann.Id, true));
        await fixture.RunAsync(fixture.World.SwitchPvpAsync(bob.Id, true));
        Assert.True(bobBody.Character.TryChangeMoney(3300), "fixture gold");
        fixture.Tick((int)(PlayerGrace.ArrivalMs / 50f) + 2);
        fixture.World.TakeTickNotices();

        Slay(fixture, field, annBody, bobBody);
        Assert.True(annBody.Murderer && !bobBody.Murderer && bobBody.SlainByPlayer, "the kill did not mark the killer");
        Assert.True(Told(fixture, ann, GameTextId.PvpMurder) is [var victim, var minutes] && victim == "Bob" && minutes == "10", "the killer was not told");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(bob)!.Value.Restart is { ExperienceShare: 0, GoldShare: 0, Scroll: false }, "the slain's restart offer costs something");
        Assert.True(RestartTransactions.Resolve(bobBody.Character, FieldState.Forest, RestartChoice.Town, slainByPlayer: true, out var experience, out var gold, out _)
            is not null && experience == 0 && gold == 0, "the slain's restart took a share");
        Assert.True(RestartTransactions.Resolve(bobBody.Character, FieldState.Forest, RestartChoice.Town, slainByPlayer: false, out _, out var share, out _)
            is not null && share == 100, "a death by a monster lost its share");
        Assert.True(ChatFixture.Latest(ann)!.Value.Entities.Single(entity => entity.EntityId == ann.Id).Appearance.Murderer, "the mark did not reach the looks");

        bobBody.Revive(bobBody.MaxLife, 0f);
        Assert.True(!bobBody.SlainByPlayer, "a revive kept the free restart");
        bobBody.MarkMurderer();
        annBody.Revive(1, 0f);
        var clean = new ChatFixture();
        var cid = await clean.JoinAsync("Cid", 0, 0, level: Fighting);
        var dan = await clean.JoinAsync("Dan", 20, 20, level: Fighting);
        var (cidBody, danBody) = (Body(clean, cid), Body(clean, dan));
        await clean.RunAsync(clean.World.SwitchPvpAsync(cid.Id, true));
        await clean.RunAsync(clean.World.SwitchPvpAsync(dan.Id, true));
        clean.Tick((int)(PlayerGrace.ArrivalMs / 50f) + 2);
        danBody.MarkMurderer();
        Slay(clean, Field(clean), cidBody, danBody);
        Assert.True(!cidBody.Murderer && danBody.SlainByPlayer, "killing a murderer marked the killer");
    }

    private static Task WireAndSettingsAsync()
    {
        var request = new byte[PvpSwitchPacket.Size];
        foreach (var on in new[] { true, false })
        {
            Assert.True(new PvpSwitchPacket(on).Write(request) == PvpSwitchPacket.Size && PvpSwitchPacket.Read(request).On == on
                && BitConverter.ToUInt32(request, 4) == EtervaleOpcodes.PvpSwitch, "the PvP switch did not round-trip");
        }

        request[10] = 2;
        Assert.Throws<InvalidDataException>(() => PvpSwitchPacket.Read(request), "a PvP switch with unknown flags was read");

        var description = new EntityDescriptionPacket(7, 1, EntityKind.Player, 60, 1, 1f, "Ann", "",
            new DescribedPlayer(PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, false, false, 0, 0) { Mounted = true, Pvp = true, Murderer = true });
        var buffer = new byte[EntityDescriptionPacket.MaximumSize];
        var read = EntityDescriptionPacket.Read(buffer.AsSpan(0, description.Write(buffer)));
        Assert.True(read.Player is { Pvp: true, Murderer: true, Mounted: true, InVillage: false }, "the description's PvP flags did not round-trip");

        var problems = new List<string>();
        var settings = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["pvp.damage_percent"] = "80" }, null, problems);
        var refused = Application.Settings.ServerSettingCatalog.Read(new Dictionary<string, string> { ["pvp.damage_percent"] = "0" }, null, problems);
        Assert.True(settings.World.PvpDamagePercent == 80 && refused.World.PvpDamagePercent == PvpRules.DefaultDamagePercent && problems.Count == 1,
            "the damage percent setting was not read or refused");
        return DamageSettingAsync(settings.World);
    }

    private static async Task DamageSettingAsync(WorldServerOptions options)
    {
        var fixture = new ChatFixture();
        await fixture.JoinAsync("Ann", 0, 0);
        var field = Field(fixture);
        Assert.True(field.PvpDamagePercent == PvpRules.DefaultDamagePercent, "a field did not start at the default damage percent");
        await fixture.RunAsync(fixture.World.ApplyOptionsAsync(options));
        Assert.True(field.PvpDamagePercent == 80, "the damage percent did not reach the field");
        Assert.True(HudSettingsPanel.PageShortcutDefaultLabel(HudSettingsPanel.PvpShortcut) == "K", "K is not the PvP shortcut");
    }

    // The attacker swings at the target (left one life) until it falls.
    private static void Slay(ChatFixture fixture, FieldInstance field, WorldPlayer attacker, WorldPlayer target)
    {
        target.ApplyDamage(target.Life - 1);
        Assert.True(field.SetAttackTarget(attacker.EntityId, target.EntityId, sustained: true), "the target was not taken");
        for (var step = 0; step < 300 && !target.IsDead; step++)
        {
            fixture.Tick(20);
        }

        Assert.True(target.IsDead, "the target did not fall");
    }

    private static FieldInstance Field(ChatFixture fixture) =>
        fixture.World.TryGetOpenField(ChatFixture.Field, out var field) ? field : throw new InvalidOperationException("the fixture field is not open");

    private static WorldPlayer Body(ChatFixture fixture, ChatFixture.Player player) =>
        ((IFieldCombatHost)Field(fixture)).TryGetPlayer(player.Id, out var body) ? body : throw new InvalidOperationException("the player is not in the field");

    // The arguments of the last line of this kind the world told the player since the last call, null for none.
    private static IReadOnlyList<string>? Told(ChatFixture fixture, ChatFixture.Player player, GameTextId id) =>
        fixture.World.TakeTickNotices().LastOrDefault(notice => notice.To == player.Id && notice.Text.Id == id) is { Text: var text } notice && notice.To != 0
            ? text.Args
            : null;
}
