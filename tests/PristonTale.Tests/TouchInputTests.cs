using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class TouchInputTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a finger landing at the lower left is the joystick and a still one there is a tap", JoystickAsync);
        yield return ("a finger dragged elsewhere turns the camera, two pinch its distance, and neither is a tap", CameraAsync);
        yield return ("touch skill buttons keep chosen learned skills and fill the rest with learned skills in order", SkillSlotsAsync);
        yield return ("the interface type picks the touch HUD on touch-only devices when automatic, round-trips, and needs the page's fingers",
            InterfaceModeAsync);
        yield return ("the touch HUD moves the minimap to the upper right corner, clear of the skill wheel, and leaves it in place otherwise", MinimapCornerAsync);
        yield return ("the quest card's Ir pauses a running guide or hunt, else starts the guide where there is a destination, else the hunt",
            QuestGoAsync);
        yield return ("the maps put a route's points where the player walks, head north up and mark the journey's hunt only in its field",
            MapMarksAsync);
        yield return ("an item's sheet offers the belt for potions, wearing for gear and use for usable items, and sends only what it offers, one at a time",
            ItemSheetAsync);
        yield return ("the status window's planned points are five amounts, none negative, some planned and no more than the free points",
            PlannedPointsAsync);
        yield return ("the NPC plate's answers name an action and, where it takes one, a button index or a browse step of one", NpcActionsAsync);
        yield return ("the combat feed tells experience and gold gained within a level, a level reached, a quest count grown and each kill",
            CombatFeedAsync);
        yield return ("the party's touch answers name a reached-player button, a member command with its 64-bit member, or a request kind",
            PartyActionsAsync);
        yield return ("the death plate picks the field only where it is open, counts down to town and reads only the field and town",
            DeathPlateAsync);
        yield return ("the rewards window's touch answers name the action and the track, and a claim its entry", RewardsActionsAsync);
        yield return ("a touch skill button takes only a learned skill that is cast, never a passive one or one not learned", SkillButtonEligibleAsync);
        yield return ("the 3D heads follow the frames the HUD editor moved, by short sides, and a shift past the limit or not a number is held",
            PortraitShiftAsync);
        yield return ("the tutorials find the starter piece still to wear, a bag potion and an empty belt", TutorialItemsAsync);
    }

    private static Task TutorialItemsAsync()
    {
        var axe = new OwnedItemView(Guid.NewGuid(), 2, "Axe", ItemKind.Axe, 1, 2, -1, 2, 3);
        var worn = new OwnedItemView(Guid.NewGuid(), 2, "Worn", ItemKind.Axe, 1, -1, (int)EquipmentSlot.Weapon, 2, 3);
        var potion = new OwnedItemView(Guid.NewGuid(), 1, "Potion", ItemKind.Potion, 5, 0, -1, 1, 1);
        var belted = new OwnedItemView(Guid.NewGuid(), 1, "Belted", ItemKind.Potion, 5, -1, PotionBelt.FirstOwnerSlot, 1, 1);
        Assert.True(GameplayHud.TouchStarterPiece([axe, potion]) == axe, "a weapon in the bag with its slot empty was not the piece to wear");
        Assert.True(GameplayHud.TouchStarterPiece([axe, worn]) is null, "a weapon was asked for while one is worn");
        Assert.True(GameplayHud.TouchBagPotion([axe, potion]) == potion && GameplayHud.TouchBagPotion([belted]) is null, "the bag potion was not found");
        Assert.True(GameplayHud.TouchBeltEmpty([axe, potion]) && !GameplayHud.TouchBeltEmpty([belted]), "the belt's emptiness was misread");
        return Task.CompletedTask;
    }

    private static Task PortraitShiftAsync()
    {
        var still = TouchPortraitPlacement.Bounds(844, 390, Vector2.Zero);
        var moved = TouchPortraitPlacement.Bounds(844, 390, new Vector2(0.5f, 0.25f));
        Assert.True(moved.X - still.X == 195 && moved.Y - still.Y is 97 or 98 && moved.Size == still.Size, $"the player's head did not follow its frame: {moved}");
        var target = TouchPortraitPlacement.TargetBounds(844, 390, new Vector2(-0.1f, 0.1f));
        var targetStill = TouchPortraitPlacement.TargetBounds(844, 390, Vector2.Zero);
        Assert.True(Math.Abs(target.X - targetStill.X + 39) <= 1 && Math.Abs(target.Y - targetStill.Y - 39) <= 1, $"the target's head did not follow its frame: {target}");
        Assert.True(TouchPortraitPlacement.Shift(float.NaN, 99f) == new Vector2(0f, TouchPortraitPlacement.MaxShift)
            && TouchPortraitPlacement.Shift(-99f, float.PositiveInfinity) == new Vector2(-TouchPortraitPlacement.MaxShift, 0f), "a wild shift was not held");
        return Task.CompletedTask;
    }

    private static Task SkillButtonEligibleAsync()
    {
        var skills = ClassSkillCatalog.For(PlayerClass.Fighter);
        var cast = skills.First(skill => skill.MouseUse != SkillMouseUse.None && SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.Kind != SkillKind.Passive);
        var passive = skills.First(skill => skill.Code != 0 && SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.Kind == SkillKind.Passive);
        Assert.True(TouchSkillSlots.Eligible(cast, 1), "a learned skill that is cast could not go on a button");
        Assert.True(!TouchSkillSlots.Eligible(cast, 0), "a skill not learned could go on a button");
        Assert.True(!TouchSkillSlots.Eligible(passive, PristonTale.Simulation.Progression.SkillLearning.MaxRank), "a passive skill could go on a button");
        return Task.CompletedTask;
    }

    private static Task DeathPlateAsync()
    {
        var panel = new HudRestartPanel(PristonTale.Engine3D.Diagnostics.NullClientLog.Instance);
        var asked = new List<PristonTale.Contracts.World.RestartChoice>();
        panel.Restart = choice =>
        {
            asked.Add(choice);
            return new TaskCompletionSource<PristonTale.Contracts.World.RestartOutcome>().Task;
        };
        panel.Update(dead: true, 0);
        Assert.True(panel.SecondsLeft(0) == 0, "a shut window counted down");
        panel.Update(dead: true, HudRestartPanel.OpenDelayMs);
        Assert.True(panel.Open && panel.SecondsLeft(HudRestartPanel.OpenDelayMs + 500) == 20, "the countdown did not start at twenty seconds");
        panel.Pick(PristonTale.Contracts.World.RestartChoice.Field, fieldOpen: false);
        Assert.True(asked.Count == 0 && panel.Open, "a closed field restart was sent");
        panel.Pick(PristonTale.Contracts.World.RestartChoice.Town, fieldOpen: false);
        Assert.True(asked.SequenceEqual([PristonTale.Contracts.World.RestartChoice.Town]) && !panel.Open, "the town was not sent");
        Assert.True(GameplayHud.TryReadRestartAction("touch-restart:Field", out var choice) && choice == PristonTale.Contracts.World.RestartChoice.Field,
            "the field choice was not read");
        Assert.True(!GameplayHud.TryReadRestartAction("touch-restart:Exit", out _) && !GameplayHud.TryReadRestartAction("touch-restart:2", out _)
            && !GameplayHud.TryReadRestartAction("touch-npc:yes", out _), "an exit, a number or a foreign action was read");
        return Task.CompletedTask;
    }

    private static Task RewardsActionsAsync()
    {
        Assert.True(GameplayHud.TryReadRewardsAction("touch-rewards:claim:Welcome:6", out var kind, out var track, out var index)
            && kind == TouchRewardsAction.Claim && track == PristonTale.Simulation.Events.RewardTrack.Welcome && index == 6, "a day claim was not read");
        Assert.True(GameplayHud.TryReadRewardsAction("touch-rewards:all:LevelRace", out kind, out track, out _) && kind == TouchRewardsAction.All
            && track == PristonTale.Simulation.Events.RewardTrack.LevelRace, "a claim of all was not read");
        Assert.True(GameplayHud.TryReadRewardsAction("touch-rewards:open:Daily", out kind, out _, out _) && kind == TouchRewardsAction.Open,
            "a refresh was not read");
        Assert.True(!GameplayHud.TryReadRewardsAction("touch-rewards:claim:Welcome", out _, out _, out _)
            && !GameplayHud.TryReadRewardsAction("touch-rewards:all:Weekly:1", out _, out _, out _), "an answer with the wrong number of parts was read");
        Assert.True(!GameplayHud.TryReadRewardsAction("touch-rewards:claim:Welcome:-1", out _, out _, out _)
            && !GameplayHud.TryReadRewardsAction("touch-rewards:claim:1:0", out _, out _, out _)
            && !GameplayHud.TryReadRewardsAction("touch-rewards:open:Monthly", out _, out _, out _), "a negative entry, a number or an unknown track was read");
        Assert.True(!GameplayHud.TryReadRewardsAction("touch-party:yes:Party", out _, out _, out _) && !GameplayHud.TryReadRewardsAction(null, out _, out _, out _),
            "a foreign action was read");
        return Task.CompletedTask;
    }

    private static Task CombatFeedAsync()
    {
        var feed = new HudCombatFeed();
        var owner = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer"));
        feed.Observe(owner);
        Assert.True(feed.Added == 0, "the first look at the character told a gain");
        feed.Observe(owner with { Experience = owner.Experience + 120, Money = owner.Money + 40 });
        Assert.True(feed.Added == 2 && feed.Newest(1) is { Kind: CombatFeedKind.Experience, Amount: 120, Sequence: 1 }
            && feed.Newest(0) is { Kind: CombatFeedKind.Gold, Amount: 40, Sequence: 2 }, "experience and gold gained were not told in order");
        feed.Observe(owner with { Level = owner.Level + 1, Experience = owner.Experience + 999 });
        Assert.True(feed.Added == 3 && feed.Newest(0) is { Kind: CombatFeedKind.Level } level && level.Amount == owner.Level + 1,
            "a level reached was not told in place of its experience");
        var quest = new QuestView(1, QuestCode.Level30, 1, "Bargon", 2, 20, QuestTimerKind.None, 0, [], []);
        feed.Observe(quest);
        feed.Observe(quest with { Kills = 3 });
        Assert.True(feed.Newest(0) is { Kind: CombatFeedKind.Quest, Count: 3, Needed: 20, Name: "Bargon" }, "a quest count grown was not told");
        for (var kill = 0; kill < HudCombatFeed.Capacity; kill++)
        {
            feed.Kill("Hopy");
        }

        Assert.True(feed.Kills == HudCombatFeed.Capacity && feed.Count == HudCombatFeed.Capacity
            && feed.Newest(HudCombatFeed.Capacity - 1).Sequence == feed.Added - HudCombatFeed.Capacity + 1, "the ring did not keep the newest lines");
        return Task.CompletedTask;
    }

    private static Task PartyActionsAsync()
    {
        Assert.True(GameplayHud.TryReadPartyAction("touch-party:player:Party", out var kind, out var value, out _) && kind == TouchPartyAction.Player
            && value == "Party", "the party invite was not read");
        Assert.True(GameplayHud.TryReadPartyAction("touch-party:player:Duel", out kind, out value, out _) && value == "Duel", "the duel button was not read");
        Assert.True(GameplayHud.TryReadPartyAction("touch-party:command:Kick:18446744073709551615", out kind, out value, out var member)
            && kind == TouchPartyAction.Command && value == "Kick" && member == ulong.MaxValue, "a kick did not keep its 64-bit member");
        Assert.True(GameplayHud.TryReadPartyAction("touch-party:no:Trade", out kind, out value, out _) && kind == TouchPartyAction.No && value == "Trade",
            "a refused trade was not read");
        Assert.True(!GameplayHud.TryReadPartyAction("touch-party:command:Create:0", out _, out _, out _)
            && !GameplayHud.TryReadPartyAction("touch-party:command:Join:5", out _, out _, out _), "a command the plates do not offer was read");
        Assert.True(!GameplayHud.TryReadPartyAction("touch-party:player:2", out _, out _, out _) && !GameplayHud.TryReadPartyAction("touch-party:yes:1", out _, out _, out _)
            && !GameplayHud.TryReadPartyAction("touch-party:command:Kick:-1", out _, out _, out _), "a number in place of a name, or a negative member, was read");
        Assert.True(!GameplayHud.TryReadPartyAction("touch-party:player", out _, out _, out _) && !GameplayHud.TryReadPartyAction("touch-party:command:Kick", out _, out _, out _)
            && !GameplayHud.TryReadPartyAction("touch-party:yes:Party:1", out _, out _, out _), "an answer with the wrong number of parts was read");
        Assert.True(!GameplayHud.TryReadPartyAction("touch-npc:yes", out _, out _, out _) && !GameplayHud.TryReadPartyAction(null, out _, out _, out _),
            "a foreign action was read");
        return Task.CompletedTask;
    }

    private static Task NpcActionsAsync()
    {
        Assert.True(GameplayHud.TryReadNpcAction("touch-npc:service:2", out var kind, out var value) && kind == TouchNpcAction.Service && value == 2,
            "a service button was not read");
        Assert.True(GameplayHud.TryReadNpcAction("touch-npc:browse:-1", out kind, out value) && kind == TouchNpcAction.Browse && value == -1,
            "a reward browse step was not read");
        Assert.True(GameplayHud.TryReadNpcAction("touch-npc:yes", out kind, out _) && kind == TouchNpcAction.Yes, "a yes was not read");
        Assert.True(!GameplayHud.TryReadNpcAction("touch-npc:yes:1", out _, out _) && !GameplayHud.TryReadNpcAction("touch-npc:choose", out _, out _),
            "an answer with the wrong number of parts was read");
        Assert.True(!GameplayHud.TryReadNpcAction("touch-npc:choose:-1", out _, out _) && !GameplayHud.TryReadNpcAction("touch-npc:browse:2", out _, out _),
            "a negative button or a browse step past one was read");
        Assert.True(!GameplayHud.TryReadNpcAction("touch-npc:3", out _, out _) && !GameplayHud.TryReadNpcAction("touch-npc:jump", out _, out _)
            && !GameplayHud.TryReadNpcAction("touch-bag:Use", out _, out _) && !GameplayHud.TryReadNpcAction(null, out _, out _), "a foreign action was read");
        Assert.True(HudNpcPanel.Said(PristonTale.Simulation.World.Npcs.NpcInteraction.Rejected(PristonTale.Contracts.Texts.GameText.None))
            .SequenceEqual([HudRequest.ConnectionError]), "a failed talk did not say the connection failed");
        return Task.CompletedTask;
    }

    private static Task PlannedPointsAsync()
    {
        var amounts = new int[5];
        Assert.True(GameplayHud.TryReadPoints("1,0,0,2,0", 3, amounts) && amounts.SequenceEqual([1, 0, 0, 2, 0]), "a valid plan was not read");
        Assert.True(!GameplayHud.TryReadPoints("1,0,0,2,1", 3, amounts) && amounts.All(points => points == 0), "a plan past the free points was read");
        Assert.True(!GameplayHud.TryReadPoints("0,0,0,0,0", 3, amounts), "an empty plan was read");
        Assert.True(!GameplayHud.TryReadPoints("1,-1,0,0,0", 3, amounts) && !GameplayHud.TryReadPoints("1,0,0", 3, amounts)
            && !GameplayHud.TryReadPoints(null, 3, amounts), "a malformed plan was read");
        return Task.CompletedTask;
    }

    private static Task ItemSheetAsync()
    {
        var potion = new OwnedItemView(Guid.NewGuid(), 1, "Potion", ItemKind.Potion, 5, 0, -1, 1, 1);
        var belted = new OwnedItemView(Guid.NewGuid(), 1, "Belted", ItemKind.Potion, 5, -1, PotionBelt.FirstOwnerSlot + 1, 1, 1);
        var axe = new OwnedItemView(Guid.NewGuid(), 2, "Axe", ItemKind.Axe, 1, 2, -1, 2, 3);
        var worn = new OwnedItemView(Guid.NewGuid(), 3, "Worn", ItemKind.Axe, 1, -1, (int)EquipmentSlot.Weapon, 2, 3);
        var scroll = new OwnedItemView(Guid.NewGuid(), 4, "Scroll", ItemKind.Misc, 1, 8, -1, 1, 1, "BI101");
        IReadOnlyList<OwnedItemView> items = [potion, belted, axe, worn, scroll];
        var offered = new List<HudItemAction>();
        HudItemActions.Offered(potion, items, offered);
        Assert.True(offered.SequenceEqual([HudItemAction.Drink, HudItemAction.BeltPut]), "a bag potion did not offer drinking and the belt");
        HudItemActions.Offered(belted, items, offered);
        Assert.True(offered.SequenceEqual([HudItemAction.BeltTake]), "a belt potion did not offer going back to the bag");
        HudItemActions.Offered(axe, items, offered);
        Assert.True(offered.SequenceEqual([HudItemAction.Equip]), "bag gear did not offer wearing it");
        HudItemActions.Offered(worn, items, offered);
        Assert.True(offered.SequenceEqual([HudItemAction.Unequip]), "worn gear did not offer taking it off");
        HudItemActions.Offered(scroll, items, offered);
        Assert.True(offered.SequenceEqual([HudItemAction.Use]), "a usable item did not offer using it");

        var state = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Sheet")) with { Items = items, InventoryRevision = 5 };
        var sent = new List<InventoryCommand>();
        var controller = new HudInventoryController(null!, command =>
        {
            sent.Add(command);
            return new TaskCompletionSource<InventoryOutcome>().Task;
        });
        controller.Update(state, HudLayout.Create(844, 390), false, default, default);
        Assert.True(!controller.Run(potion, HudItemAction.Equip, -1, offered) && sent.Count == 0, "an action the item does not offer was sent");
        Assert.True(!controller.Run(potion, HudItemAction.BeltPut, PotionBelt.Boxes, offered) && sent.Count == 0, "a belt box past the belt was sent");
        Assert.True(controller.Run(potion, HudItemAction.BeltPut, 2, offered) && controller.Busy, "a belt put was not sent");
        Assert.True(sent[0] is { Action: InventoryAction.BeltPut, Destination: 2, ExpectedRevision: 5 } && sent[0].ItemId == potion.InstanceId,
            "the belt put did not name its box and item");
        Assert.True(!controller.Run(belted, HudItemAction.BeltTake, -1, offered) && sent.Count == 1, "a second action went out before the first answered");

        var taking = new List<InventoryCommand>();
        var other = new HudInventoryController(null!, command =>
        {
            taking.Add(command);
            return new TaskCompletionSource<InventoryOutcome>().Task;
        });
        other.Update(state, HudLayout.Create(844, 390), false, default, default);
        Assert.True(other.Run(belted, HudItemAction.BeltTake, -1, offered) && taking[0] is { Action: InventoryAction.BeltTake, Destination: -1 },
            "taking a potion off the belt did not let the server find its place");
        return Task.CompletedTask;
    }

    private static Task MapMarksAsync()
    {
        var guide = new RoutePoint(FieldCodes.Ricarten, 1200f, -4000f);
        var render = HudMinimap.RenderPoint(guide);
        var planar = WorldSpace.Planar(guide.X, guide.Z);
        Assert.True(render.X == planar.X && render.Z == planar.Y, "a route's point is not where the guide's route walks to");
        var rectangle = new PristonIntRectangle(1000 * 256, -5000 * 256, 2000 * 256, -3000 * 256);
        Assert.True(HudMinimap.MapPoint(rectangle, render) == new Vector2(200f, 1000f), "a route's point is not in the map's units, y south from the top");
        Assert.True(HudMinimap.Heading(new Vector3(0f, 0f, -1f)) == 0f && MathF.Abs(HudMinimap.Heading(Vector3.UnitX) - MathF.PI / 2) < 1e-5f
            && HudMinimap.Heading(Vector3.Zero) == 0f, "the heading is not clockwise from north");
        var route = AdventurerJourneyRoute.Tempskron;
        Assert.True(route.HuntAreaIn(route.HuntField) == route.HuntArea && route.HuntAreaIn(FieldCodes.Ricarten) is null,
            "the hunting area was not marked in its field alone");
        var layout = HudLayout.Create(844, 390) with { Browser = true, Touch = true };
        Assert.True(HudMinimap.Box(layout).Top == 10, "the touch minimap kept room for a title above it");
        return Task.CompletedTask;
    }

    private static Task QuestGoAsync()
    {
        Assert.True(TouchQuestGo.Next(guiding: true, hunting: false, canGuide: true) == TouchQuestStep.Pause, "a running guide did not pause");
        Assert.True(TouchQuestGo.Next(guiding: false, hunting: true, canGuide: true) == TouchQuestStep.Pause, "a running hunt did not pause");
        Assert.True(TouchQuestGo.Next(guiding: false, hunting: false, canGuide: true) == TouchQuestStep.Guide, "a destination did not start the guide");
        Assert.True(TouchQuestGo.Next(guiding: false, hunting: false, canGuide: false) == TouchQuestStep.Hunt, "without a destination it did not hunt");
        return Task.CompletedTask;
    }

    private static Task JoystickAsync()
    {
        var fingers = new Fingers();
        var orbit = new TouchOrbit(fingers);
        fingers.Set(new TouchFinger(1, .2f, .8f));
        orbit.Update();
        Assert.True(orbit.JoystickActive && orbit.Movement == Vector2.Zero, "a finger at the lower left did not take the joystick");
        fingers.Set(new TouchFinger(1, .3f, .8f));
        orbit.Update();
        Assert.True(orbit.Movement.X > .99f && MathF.Abs(orbit.Movement.Y) < .01f, "dragging the joystick right did not move right");
        fingers.Set();
        orbit.Update();
        Assert.True(!orbit.JoystickActive && orbit.Movement == Vector2.Zero && orbit.Tap is null, "lifting a moved joystick did not stop it");

        fingers.Set(new TouchFinger(2, .2f, .8f));
        orbit.Update();
        fingers.Set();
        orbit.Update();
        Assert.True(orbit.Tap == new Vector2(.2f, .8f), "a still finger in the joystick region was not a tap");

        fingers.Set(new TouchFinger(3, .2f, .8f));
        orbit.Update(allowJoystick: false);
        Assert.True(!orbit.JoystickActive, "the joystick was taken while it was not allowed");
        return Task.CompletedTask;
    }

    private static Task CameraAsync()
    {
        var fingers = new Fingers();
        var orbit = new TouchOrbit(fingers);
        var start = orbit.Offset;
        fingers.Set(new TouchFinger(1, .7f, .3f));
        orbit.Update();
        fingers.Set(new TouchFinger(1, .8f, .3f));
        orbit.Update();
        Assert.True(orbit.CameraActive && !orbit.JoystickActive, "a finger at the upper right did not turn the camera");
        Assert.True(Vector3.Distance(start, orbit.Offset) > 1f && MathF.Abs(start.Length() - orbit.Offset.Length()) < .01f,
            "a drag did not turn the camera around the player");
        fingers.Set();
        orbit.Update();
        Assert.True(orbit.Tap is null, "a drag was taken as a tap");

        var turned = orbit.Offset.Length();
        fingers.Set(new TouchFinger(4, .6f, .3f), new TouchFinger(5, .7f, .3f));
        orbit.Update();
        fingers.Set(new TouchFinger(4, .5f, .3f), new TouchFinger(5, .8f, .3f));
        orbit.Update();
        Assert.True(orbit.Offset.Length() < turned, "spreading two fingers did not bring the camera closer");
        fingers.Set();
        orbit.Update();
        Assert.True(orbit.Tap is null, "a pinch was taken as a tap");
        return Task.CompletedTask;
    }

    private static Task SkillSlotsAsync()
    {
        var usable = SkillRuleCatalog.All.Select(rule => rule.Code).Distinct().Take(5).ToArray();
        Assert.True(usable.Length == 5, "the skill catalog has fewer than five skills");
        OwnedSkillView[] skills = [new(usable[0], 1), new(usable[1], 0), new(usable[2], 1), new(usable[3], 1), new(usable[4], 1)];
        var codes = new uint[TouchSkillSlots.Count];
        var chosen = new bool[TouchSkillSlots.Count];
        codes[1] = usable[3];
        chosen[1] = true;
        codes[2] = usable[1];
        chosen[2] = true;
        TouchSkillSlots.Fill(skills, codes, chosen);
        Assert.True(codes[1] == usable[3] && chosen[1], "a chosen learned skill left its button");
        Assert.True(!chosen[2], "a chosen skill without a rank kept its button");
        Assert.True(codes[0] == usable[0] && codes[2] == usable[2] && codes[3] == usable[4],
            "the other buttons did not take the learned skills in order, skipping the chosen one");
        Assert.True(TouchSkillSlots.ButtonCaption("Golpe Duplo") == "Golpe" && TouchSkillSlots.ButtonCaption("Tornado") == "Tornado"
            && TouchSkillSlots.ButtonCaption("Avalanche") == "Avalan…" && TouchSkillSlots.ButtonCaption("") == "",
            "a skill button's caption is not the first word cut to seven characters");
        return Task.CompletedTask;
    }

    private static Task InterfaceModeAsync()
    {
        Assert.True(new ClientSettings().UsesTouch(true) && !new ClientSettings().UsesTouch(false), "automatic did not follow the device");
        Assert.True(new ClientSettings { InterfaceMode = InterfaceMode.Touch }.UsesTouch(false), "touch was not used where chosen");
        Assert.True(!new ClientSettings { InterfaceMode = InterfaceMode.Pc }.UsesTouch(true), "PC was not kept on a touch device");
        var parsed = ClientSettingsStore.Parse("{\"version\":1,\"interfaceMode\":\"Touch\"}", NullClientLog.Instance);
        Assert.True(parsed?.InterfaceMode == InterfaceMode.Touch, "the document's interface type was not read");
        var saved = ClientSettingsStore.Parse(ClientSettingsStore.Serialize(new ClientSettings { InterfaceMode = InterfaceMode.Pc }), NullClientLog.Instance);
        Assert.True(saved?.InterfaceMode == InterfaceMode.Pc, "the interface type did not round-trip");

        // The canvas leaves out what the page's touch HUD replaces only where the page reports fingers.
        var touch = new ClientSettings { InterfaceMode = InterfaceMode.Touch };
        var (fingers, device) = (WebWorldHud.Fingers, WebWorldHud.TouchDevice);
        try
        {
            WebWorldHud.Fingers = null;
            Assert.True(!WebWorldHud.TouchActive(touch), "the touch HUD was in use without the page's fingers");
            WebWorldHud.Fingers = new Fingers();
            WebWorldHud.TouchDevice = false;
            Assert.True(WebWorldHud.TouchActive(touch) && !WebWorldHud.TouchActive(new ClientSettings()), "the interface type did not decide the touch HUD");
            WebWorldHud.TouchDevice = true;
            Assert.True(WebWorldHud.TouchActive(new ClientSettings()), "automatic did not use the touch HUD on a touch-only device");
        }
        finally
        {
            (WebWorldHud.Fingers, WebWorldHud.TouchDevice) = (fingers, device);
        }

        return Task.CompletedTask;
    }

    private static Task MinimapCornerAsync()
    {
        var layout = HudLayout.Create(844, 390) with { Browser = true };
        var box = HudMinimap.Box(layout with { Touch = true });
        Assert.True(box.Right < layout.Width && box.Top > 0 && box.Bottom < layout.Height / 2 && box.Right > layout.Width - 20,
            "the touch HUD's minimap is not in the upper right corner");
        Assert.True(HudMinimap.Box(layout).Bottom > layout.Height / 2, "the minimap left the lower right corner without the touch HUD");
        return Task.CompletedTask;
    }

    private sealed class Fingers : ITouchFingers
    {
        private TouchFinger[] down = [];

        public int Count => down.Length;

        public void Set(params TouchFinger[] fingers) => down = fingers;

        public bool TryRead(int index, out TouchFinger finger)
        {
            finger = index < down.Length ? down[index] : default;
            return index < down.Length;
        }
    }
}
