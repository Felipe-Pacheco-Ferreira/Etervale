using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Application.World;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.Physics;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class ClientTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original presence packets retain combat events, skill motion and field identity", PresenceAsync);
        yield return ("movement reports go out on change, at 5 Hz moving and 1 Hz standing, never twice within 50 ms", MovementPacingAsync);
        yield return ("crowded presence keeps position updates inside the per-receiver budget without starving walkers", CrowdBudgetAsync);
        yield return ("remote rates and explicit swing events replace per-frame restarts", RemoteMotionAsync);
        yield return ("NPC duplicate Stand clips preserve exact INX indices and weighted selection", NpcStandMotionsAsync);
        yield return ("pending actor preparation does not block presence or resurrect removals", PendingActorAsync);
        yield return ("a monster whose model downloads first is prepared after the download and appears", DownloadedModelAsync);
        yield return ("remote models appear only after their texture uploads finish", PendingModelTexturesAsync);
        yield return ("remote height follows the server sample instead of the highest floor", ServerHeightAsync);
        yield return ("NPC, monster and player collision survives pending assets", CollisionRosterAsync);
        yield return ("an action field draws actors facing left or right, finds the nearest one ahead on the player's line and the player a walk bumps into, never one it walks away from", SideViewAsync);
        yield return ("cursor rays originate at the camera and reject an enclosing NPC pick volume", CursorPickingAsync);
        yield return ("auto-quest subtitle timing keeps short lines visible and scales with words", QuestSubtitleTimingAsync);
        yield return ("the free auto-hunt radius is clamped and uses the world metre", FreeHuntRadiusAsync);
        yield return ("free auto-hunt applies monster and loot filters in their specified precedence", FreeHuntFiltersAsync);
        yield return ("free auto-hunt categorizes catalog drops and protects potion quotas", FreeHuntLootCatalogAsync);
        yield return ("free auto-hunt chooses the smallest guaranteed potion and respects a preferred choice", FreeHuntPotionsAsync);
        yield return ("free auto-hunt profile transfer preserves filters and rejects malformed codes", FreeHuntProfileAsync);
    }

    private static Task FreeHuntProfileAsync()
    {
        var settings = new FreeHuntSettings
        {
            Radius = 1024, OnlyMonsters = "Hopy, Aranha de Aço", ShareAccount = true,
            Skills = [12, 34], SkillManaFloor = new Dictionary<uint, int> { [12] = 50 }, AlwaysLoot = "@Weapons"
        };
        var code = FreeHuntProfile.Encode(settings);
        Assert.True(FreeHuntProfile.TryDecode(" " + code + "\n", out var imported), "valid profile code refused");
        Assert.True(imported.Radius == 1024 && imported.OnlyMonsters == settings.OnlyMonsters && imported.AlwaysLoot == settings.AlwaysLoot
            && imported.Skills.SequenceEqual(settings.Skills) && imported.SkillManaFloor[12] == 50 && !imported.ShareAccount,
            "profile lost options or exported the local account preference");
        Assert.True(!FreeHuntProfile.TryDecode("ETERVALE-HUNT-1:***", out _)
            && !FreeHuntProfile.TryDecode(FreeHuntProfile.Encode(settings with { HP = null }), out _)
            && !FreeHuntProfile.TryDecode(FreeHuntProfile.Encode(settings with { SummonCount = int.MaxValue }), out _)
            && !FreeHuntProfile.TryDecode(FreeHuntProfile.Encode(settings with { TargetTimeout = int.MaxValue }), out _),
            "invalid profile accepted");
        return Task.CompletedTask;
    }

    private static Task FreeHuntFiltersAsync()
    {
        var filters = new FreeHuntSettings
        {
            OnlyMonsters = "Hopy, Rabie", IgnoredMonsters = "wild", MinimumLevel = 2, MaximumLevel = 6
        };
        Assert.True(filters.AcceptsMonster("Rabie", 5, false) && !filters.AcceptsMonster("Wild Hopy", 5, false)
            && !filters.AcceptsMonster("Hopy", 7, false), "monster filters or levels were ignored");
        Assert.True(filters.AcceptsMonster("Attacking enemy", 99, true)
            && !(filters with { StrictMonsterFilter = true }).AcceptsMonster("Attacking enemy", 99, true), "strict retaliation filter changed");
        Assert.True(FreeHuntSettings.WorldRadius(640) == 250f && FreeHuntSettings.WorldRadius(320) == 125f, "the panel's default circles differ");

        var owner = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Knight, "Hunter", 20));
        var spec = (uint)JobBitCode.Knight;
        var drop = new GroundDropView(Guid.NewGuid(), ItemCodes.Parse("WS228"), "WS228", "Legends Sword", 1, false, default)
        {
            RequiredLevel = 130, Specialization = spec
        };
        var limits = filters with { OnlyOwnSpec = true, MinimumItemLevel = 100, MaximumItemLevel = 140 };
        Assert.True(limits.AcceptsDrop(drop, owner, spec)
            && !limits.AcceptsDrop(drop with { RequiredLevel = 90 }, owner, spec)
            && !limits.AcceptsDrop(drop with { Specialization = (uint)JobBitCode.Fighter }, owner, spec)
            && !limits.AcceptsDrop(drop with { ThrownByReceiver = true }, owner, spec), "level, spec or own-drop rules failed");
        var lists = limits with { Excluded = LootHuntCategory.Weapons, AlwaysLoot = "@Weapons" };
        Assert.True(lists.AcceptsDrop(drop, owner, spec)
            && !(lists with { NeverLoot = "legends" }).AcceptsDrop(drop, owner, spec), "the always/never list precedence changed");
        return Task.CompletedTask;
    }

    private static Task FreeHuntLootCatalogAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Knight, "Hunter", 20);
        var life = new ItemDefinition(ItemCodes.Parse("PL101"), "Poção Pequena de Vida", ItemKind.Potion, MaxStack: PotionDefinition.StackSize)
        {
            CatalogKey = "PL101", Potion = new PotionDefinition(10, 12, 0, 0, 0, 0)
        };
        Assert.True(character.Inventory.TryAdd(new ItemInstance(life, 8)), "fixture potion did not fit");
        var owner = OwnerState.Capture(character);
        var lifeDrop = Ground("PL101", life.Name, count: 3);
        var limits = new FreeHuntSettings { MaxLifePotions = 10, AlwaysLoot = "@HP" };
        Assert.True(!limits.AcceptsDrop(lifeDrop, owner, 0) && limits.AcceptsDrop(lifeDrop with { Amount = 2 }, owner, 0),
            "incoming stack crossed the limit or a stack within the limit was skipped");

        var imported = new FreeHuntSettings { Excluded = LootHuntCategory.Other }.UpgradeLootCategories();
        var excluded = imported with { Excluded = imported.Excluded & ~LootHuntCategory.Potions };
        Assert.True(!imported.AcceptsDrop(lifeDrop, owner, 0) && excluded.UpgradeLootCategories().AcceptsDrop(lifeDrop, owner, 0),
            "the old exclusion was lost, or a later toggle was migrated again");

        Assert.True(Cat("PL101") == LootHuntCategory.Potions && Cat("FO101") == LootHuntCategory.Force
            && Cat("PR301") == LootHuntCategory.Runes && Cat("PR201") == LootHuntCategory.Materials
            && Cat("EC101") == LootHuntCategory.Travel && Cat("BI113") == LootHuntCategory.Premium
            && Cat("BI136") == LootHuntCategory.Masteries && Cat("SP105") == LootHuntCategory.Events && Cat("QT101") == LootHuntCategory.Quest,
            "a drop was classified outside its real catalog family");

        var filter = new FreeHuntSettings { LootMode = LootHuntMode.ListedOnly, AlwaysLoot = " #PL101 , @RunesPlus , @MP " };
        Assert.True(filter.AcceptsDrop(lifeDrop, owner, 0) && filter.AcceptsDrop(Ground("PM101", "Mana"), owner, 0)
            && !filter.AcceptsDrop(Ground("PR301", "Runes"), owner, 0) && !filter.AcceptsDrop(Ground("PL102", "PL101 Plus"), owner, 0),
            "exact item or category selectors matched a different drop");

        var priority = new FreeHuntSettings { AlwaysLoot = "@Runes", PrioritizeOwnSpec = true };
        const uint spec = (uint)JobBitCode.Knight;
        var rune = priority.LootPriority(Ground("PR301", "Rune"), owner, spec);
        var weapon = priority.LootPriority(Ground("WS201", "Sword") with { Specialization = spec }, owner, spec);
        var quest = priority.LootPriority(Ground("QT101", "Quest"), owner, spec);
        Assert.True(quest > rune && rune > weapon && weapon > priority.LootPriority(lifeDrop, owner, spec),
            "collection priority did not honor quest, pinned and matching specialization order");
        return Task.CompletedTask;

        static LootHuntCategory Cat(string key) => FreeHuntSettings.Category(key, ItemCodes.Parse(key), false);
        static GroundDropView Ground(string key, string name, int count = 1) => new(Guid.NewGuid(), ItemCodes.Parse(key), key, name, count, false, default);
    }

    private static Task FreeHuntPotionsAsync()
    {
        static OwnedItemView Potion(string key, int min, int max, int slot = -1) =>
            new(Guid.NewGuid(), ItemCodes.Parse(key), key, ItemKind.Potion, 3, 0, slot, 1, 1, key)
            {
                Potion = new PotionDefinition(min, max, 0, 0, 0, 0)
            };

        var small = Potion("PL105", 20, 30);
        var medium = Potion("PL102", 40, 45, PotionBelt.FirstOwnerSlot);
        var large = Potion("PL103", 80, 100);
        OwnedItemView[] potions = [large, medium, small];
        Assert.True(HuntPotionSelection.Select(potions, PotionResource.Life, "", 40) == medium, "oversized potion was used");
        Assert.True(HuntPotionSelection.Select(potions, PotionResource.Life, "", 15) == small, "catalog order beat authored potency");
        Assert.True(HuntPotionSelection.Select(potions, PotionResource.Life, "", 120) == large, "strongest insufficient dose was skipped");
        Assert.True(HuntPotionSelection.Select(potions, PotionResource.Life, "pl105", 40) == small, "explicit preference was ignored");
        Assert.True(HuntPotionSelection.Select(potions, PotionResource.Life, "", 40, bagOnly: true) == large, "belt refill selected a belt item");
        Assert.True(HuntPotionSelection.Select([small with { Potion = null }], PotionResource.Life, "", 40)?.InstanceId == small.InstanceId,
            "server without potency extension lost its fallback");

        var buffer = new byte[PotionPotencyPacket.HeaderSize + PotionPotencyEntry.Size];
        var entry = new PotionPotencyEntry(small.InstanceId, 20, 30, 0, 0, 0, 0);
        var packet = new PotionPotencyPacket(72, [entry]);
        var written = packet.Write(buffer);
        var decoded = PotionPotencyPacket.Read(buffer.AsSpan(0, written));
        Assert.True(decoded.Revision == 72 && decoded.Items.Single() == entry, "the server-authored range did not round-trip");
        return Task.CompletedTask;
    }

    private static Task QuestSubtitleTimingAsync()
    {
        Assert.True(HudQuestDocuments.ReadingTime("Olá!") == TimeSpan.FromSeconds(5.5),
            "the short subtitle disappeared before its minimum reading time");
        var subtitle = "Atravesse a ponte com cuidado\r\n e procure o instrutor da aldeia para\t começar seu treinamento.";
        Assert.True(HudQuestDocuments.ReadingTime(subtitle) == TimeSpan.FromSeconds(6.3),
            "the subtitle timer did not count the words across line breaks and whitespace");
        return Task.CompletedTask;
    }

    private static Task FreeHuntRadiusAsync()
    {
        Assert.True(FreeHuntRange.Clamp(int.MinValue) == FreeHuntRange.MinimumMetres && FreeHuntRange.InWorldUnits(0) == 125f,
            "the configured radius fell below five metres");
        Assert.True(FreeHuntRange.InWorldUnits(FreeHuntRange.DefaultMetres) == 375f, "the default radius differs from fifteen metres");
        Assert.True(FreeHuntRange.Clamp(int.MaxValue) == FreeHuntRange.MaximumMetres && FreeHuntRange.InWorldUnits(int.MaxValue) == 750f,
            "the configured radius exceeded thirty metres");
        return Task.CompletedTask;
    }

    private static Task MovementPacingAsync()
    {
        var pacer = new PristonTale.Gameplay.World.MovementReportPacer();
        pacer.SetField(3);
        Assert.True(pacer.TryTake(false, false, 0, 3), "the first report did not go out");
        Assert.True(!pacer.TryTake(true, false, 0, 3), "a change inside the 50 ms gap went out at once");
        pacer.Advance(0.05f);
        Assert.True(pacer.TryTake(true, false, 0, 3), "the change did not go out once the gap passed");
        pacer.Advance(0.1f);
        Assert.True(!pacer.TryTake(true, false, 0, 3), "an unchanged walk reported before 200 ms");
        pacer.Advance(0.11f);
        Assert.True(pacer.TryTake(true, false, 0, 3), "an unchanged walk did not report at 200 ms");
        pacer.Advance(0.06f);
        pacer.Request();
        Assert.True(pacer.TryTake(true, false, 0, 3), "a requested report did not go out after the gap");
        pacer.Advance(0.06f);
        Assert.True(pacer.TryTake(false, false, 0, 3), "a stop did not report");
        pacer.Advance(0.9f);
        Assert.True(!pacer.TryTake(false, false, 0, 3), "a standing player reported before a second");
        pacer.Advance(0.11f);
        Assert.True(pacer.TryTake(false, false, 0, 3), "a standing player did not report after a second");

        // A cast reports its position at once, inside the gap; the pacing does not send that state again.
        pacer.Advance(0.02f);
        pacer.Request();
        pacer.Taken(false, false, 0, 3);
        Assert.True(!pacer.TryTake(false, false, 0, 3), "the position reported ahead of a cast went out again");
        pacer.Advance(1.01f);
        Assert.True(pacer.TryTake(false, false, 0, 3), "the pacing did not resume after a cast's report");
        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        var origin = new System.Numerics.Vector3(120, 35, -400);
        var impact = new CombatEventView(9, 1, CombatEventKind.MonsterSkillImpact,
            sourceSoundCode: 0x18E0, position: origin, facing: 1.2f);
        var entity = new EntityView(2, EntityKind.Monster, false, origin, 1.2f, MotionState.Skill,
            10, 45, 60, default, templateId: 1);
        var self = new EntityView(1, EntityKind.Player, false, origin, 0f, MotionState.Stand, 1, 100, 100,
            new PlayerAppearanceView("Self", PlayerClass.Mechanician, 0, 0, 0, "", ""));
        var snapshot = new WorldSnapshot(8, [self, entity], [impact], 3);
        var update = new WireRoundTrip(1).PushMerged(snapshot);
        Assert.True(update.FieldId == 3 && update.Spawns.Single(spawn => spawn.EntityId == 2).MoveState == MotionState.Skill,
            "TCP presence dropped field or skill motion");
        Assert.True(update.Spawns.Single(spawn => spawn.EntityId == 1).IsSelf, "own avatar was not marked as self");
        Assert.True(update.Events.Single().Position == origin && update.Events.Single().Kind == CombatEventKind.MonsterSkillImpact,
            "TCP presence dropped skill impact data");
        Assert.True(update.Spawns.All(spawn => spawn.ServerTimeMs == 8 * 50), "presence samples lost the server tick stamp");
        return Task.CompletedTask;
    }

    private static Task CrowdBudgetAsync()
    {
        // The largest crowd a receiver shows at once: the receiver, MaxVisiblePlayers other players and monsters to MaxVisible.
        EntityView Walker(int id, float offset) => id <= Server.Net.PresenceEncoder.MaxVisiblePlayers + 1
            ? new((uint)id, EntityKind.Player, false, new System.Numerics.Vector3(id + offset, 0, id % 20 * 10f), 0f, MotionState.Walk, 1, 100, 100,
                new PlayerAppearanceView("W" + id, PlayerClass.Mechanician, 0, 0, 0, "", ""))
            : new((uint)id, EntityKind.Monster, false, new System.Numerics.Vector3(id + offset, 0, id % 20 * 10f), 0f, MotionState.Walk, 1, 100, 100,
                default, templateId: 5);
        var crowd = Server.Net.PresenceEncoder.MaxVisible + 1;
        var wire = new WireRoundTrip(1);
        var spawned = wire.PushMerged(new WorldSnapshot(1, Enumerable.Range(1, crowd).Select(id => Walker(id, 0)).ToArray(), fieldId: 3));
        Assert.True(spawned.Spawns.Count == crowd, "spawns were held back by the movement budget");
        var updated = new HashSet<ulong>();
        for (uint tick = 2; tick < 42; tick++)
        {
            var before = wire.BytesSent;
            var update = wire.PushMerged(new WorldSnapshot(tick, Enumerable.Range(1, crowd).Select(id => Walker(id, tick)).ToArray(), fieldId: 3));
            // Budget for others, plus the receiver's own entry, at most two PLAYDATAGROUP headers and the tick's clock.
            Assert.True(wire.BytesSent - before <= 2048 + 84 + 2 * 16 + PresenceClockPacket.Size, "a crowd exceeded the per-receiver movement budget");
            updated.UnionWith(update.States.Select(state => state.EntityId));
        }

        Assert.True(updated.Count == crowd, "age weighting starved part of the crowd");
        return Task.CompletedTask;
    }

    private static Task RemoteMotionAsync()
    {
        var scene = new FakeScene();
        var remote = new RemoteModelEntity(
            scene,
            ActorHandle.None,
            1,
            0,
            Vector3.Zero,
            0,
            MotionState.Attack,
            100);
        var now = 1000d;
        void Settle()
        {
            for (var frame = 0; frame < 4; frame++)
            {
                now += 50;
                remote.Update(0.05f, now);
            }
        }

        var swing = new CombatEventInfo(2, 1, CombatEventKind.SwingBegin, 800, 400, 0, false, false);
        remote.SetRates(5, 1.75f);
        remote.ApplyCombatEvent(swing, now);
        Settle();
        Assert.True(scene.Restarts == 1, "swing did not restart exactly once");
        Assert.Near(1.75f, scene.LastRate);
        var commands = scene.Commands;
        for (var index = 0; index < 10; index++)
        {
            now += 16;
            remote.Update(0.016f, now);
        }

        Assert.True(scene.Commands == commands, "held Attack emitted per-frame commands");
        remote.ApplyCombatEvent(swing, now);
        Settle();
        Assert.True(scene.Restarts == 2, "second swing event was lost");
        remote.SetTarget(now, Vector3.Zero, 0, MotionState.Skill, 100);
        var cast = new CombatEventInfo(2, 1, CombatEventKind.SwingBegin, 1980, 957, 0, false, false);
        remote.ApplyCombatEvent(cast, now);
        Settle();
        Assert.True(scene.Restarts == 3 && remote.Motion == MotionCode.Skill, "skill entry restarted basic attack");
        remote.ApplyCombatEvent(cast, now);
        Settle();
        Assert.True(scene.Restarts == 4 && remote.Motion == MotionCode.Skill, "repeated skill did not restart its own motion");
        remote.SetTarget(now, Vector3.Zero, 0, MotionState.Walk, 100);
        Settle();
        Settle();
        Assert.True(remote.ShownMotion == MotionCode.Stand, "Walk without ground motion was shown walking");
        Assert.Near(1f, scene.LastRate);
        return Task.CompletedTask;
    }

    private static Task NpcStandMotionsAsync()
    {
        var motions = Enumerable.Range(0, 13)
            .Select(static _ => Motion(0, 0, 0))
            .ToArray();
        motions[10] = Motion((uint)MotionCode.Walk, 11, 41);
        motions[11] = Motion((uint)MotionCode.Stand, 50, 90);
        motions[12] = Motion((uint)MotionCode.Stand, 100, 160);
        var warehouseInfo = ModelInfo("TN-010.inx", motions);
        var cycle = new NpcStandMotionCycle(warehouseInfo, 91);
        var selected = new HashSet<int>();
        for (var index = 0; index < 32; index++)
        {
            var update = cycle.Update(2f, MotionCode.Stand);
            Assert.True(update.MotionChanged, "completed NPC Stand cycle did not select another concrete clip");
            Assert.True(update.MotionIndex is 11 or 12, "NPC idle selected Walk or an empty reserved INX slot");
            selected.Add(update.MotionIndex);
        }

        Assert.True(selected.SetEquals([11, 12]), "warehouse keeper never reached every authored Stand gesture");

        var weightedRates = new int[30];
        weightedRates[PristonFormatConstants.CharacterMotionBase] = 100;
        var weightedInfo = warehouseInfo with
        {
            NpcMotionRates = weightedRates,
            NpcMotionSelection = Enumerable.Repeat(12, PristonFormatConstants.MotionRateSelectionCount).ToArray()
        };
        var weighted = new NpcStandMotionCycle(weightedInfo, 17).Update(0f, MotionCode.Stand);
        Assert.True(weighted.MotionIndex == 12, "expanded Win32 NPC motion-rate table was ignored");

        var scene = new FakeScene();
        var npc = new RemoteModelEntity(
            scene,
            ActorHandle.None,
            1f,
            0,
            Vector3.Zero,
            0f,
            MotionState.Stand,
            1,
            EntityKind.Npc,
            modelInfo: warehouseInfo,
            entityId: 91);
        npc.Update(0f, 0);
        Assert.True(scene.ExactMotionIndices.Count == 1 && scene.ExactMotionIndices[0] is 11 or 12, "remote NPC collapsed its selected Stand entry back to the enum");
        return Task.CompletedTask;
    }

    private static PristonMotionInfo Motion(uint state, uint startFrame, uint endFrame)
    {
        return new PristonMotionInfo(state, startFrame, endFrame, [], 0, 0, 0, 1, 0, 0);
    }

    private static PristonModelInfo ModelInfo(string path, IReadOnlyList<PristonMotionInfo> motions)
    {
        var emptyGroup = new PristonModelGroup(0, []);
        return new PristonModelInfo(path, "fixture.smd", "fixture.smd", "", emptyGroup, emptyGroup, emptyGroup, motions, (uint)motions.Count, "", "", "", [], 0);
    }

    private static Task PendingActorAsync()
    {
        var scene = new FakeScene();
        var composer = new PendingComposer();
        var manager = new RemoteEntityManager(scene, composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo(
            "remote",
            PlayerClass.Fighter,
            0,
            0,
            0,
            "",
            "",
            1);
        var spawn = new EntitySpawnInfo(
            2,
            EntityKind.Player,
            false,
            System.Numerics.Vector3.Zero,
            0,
            MotionState.Stand,
            100,
            100,
            appearance,
            0);
        manager.Apply(new PresenceUpdate(1, [spawn], [], [], []));
        Assert.True(manager.Count == 0 && composer.Composed == 0, "unprepared actor was composed");
        manager.Apply(new PresenceUpdate(2, [], [], [2], []));
        composer.Ready.TrySetResult();
        manager.Update(0.05f);
        Assert.True(manager.Count == 0 && composer.Composed == 0, "removed pending actor reappeared");
        return Task.CompletedTask;
    }

    private static async Task DownloadedModelAsync()
    {
        var composer = new PendingComposer();
        var catalog = new WorldRenderCatalog();
        catalog.Register(new MonsterRenderInfo(1, "char/monster/Hopy/hopy.inx", 1f, "Hopy", 3));
        var download = new TaskCompletionSource();
        var asked = 0;
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".", catalog)
        {
            ModelAssets = _ => ++asked == 1 ? download.Task : null
        };
        manager.Apply(new PresenceUpdate(1,
            [new(2, EntityKind.Monster, false, System.Numerics.Vector3.Zero, 0, MotionState.Stand, 10, 10, null, 1)], [], [], []));
        manager.Update(0.05f);
        Assert.True(manager.Count == 0, "the monster appeared before its model downloaded");
        download.SetResult();
        manager.Update(0.05f);
        composer.Ready.TrySetResult();
        await Task.Yield();
        for (var frame = 0; frame < 3 && manager.Count == 0; frame++)
        {
            await Task.Delay(10);
            manager.Update(0.05f);
        }

        Assert.True(manager.Count == 1 && composer.Composed == 1, "the monster never appeared after its model downloaded");
    }

    private static Task PendingModelTexturesAsync()
    {
        var composer = new PendingComposer { TexturesReady = false };
        var catalog = new WorldRenderCatalog();
        catalog.Register(new MonsterRenderInfo(1, "char/monster/Hopy/hopy.inx", 1f, "Hopy", 3));
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".", catalog);
        var spawn = new EntitySpawnInfo(2, EntityKind.Monster, false, System.Numerics.Vector3.Zero, 0, MotionState.Stand, 10, 10, null, 1);
        composer.Ready.SetResult();
        manager.Apply(new PresenceUpdate(1, [spawn, spawn with { EntityId = 3 }], [], [], []));
        manager.Update(0.05f);
        Assert.True(composer.Composed == 0, "a remote model appeared while its textures were uploading");

        manager.Apply(new PresenceUpdate(2, [], [], [2], []));
        composer.TexturesReady = true;
        manager.Update(0.05f);
        manager.Update(0.05f);
        Assert.True(manager.Count == 1 && composer.Composed == 1, "the remaining model failed to appear or a removed model reappeared");
        return Task.CompletedTask;
    }

    private static Task SideViewAsync()
    {
        Assert.True(PristonTale.Gameplay.World.ActionFieldView.Facing(0.3f) == MathF.PI / 2f
            && PristonTale.Gameplay.World.ActionFieldView.Facing(-2.5f) == -MathF.PI / 2f
            && PristonTale.Gameplay.World.ActionFieldView.Facing(0f) == MathF.PI / 2f, "a facing did not turn to its side");
        var remote = new RemoteModelEntity(new FakeScene(), ActorHandle.None, 1, 0, Vector3.Zero, 2.8f, MotionState.Stand, 1) { SideView = true };
        remote.Update(0.05f, 50);
        Assert.True(remote.RenderFacing == MathF.PI / 2f, $"a side-view actor faces {remote.RenderFacing}");

        var composer = new PendingComposer();
        composer.Ready.TrySetResult();
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo("Remote", PlayerClass.Fighter, 0, 0, 0, "", "", 1);
        manager.Apply(new PresenceUpdate(1,
        [
            new(2, EntityKind.Player, false, new System.Numerics.Vector3(60, 0, 10), 0, MotionState.Stand, 100, 100, appearance, 0),
            new(3, EntityKind.Player, false, new System.Numerics.Vector3(30, 0, 40), 0, MotionState.Stand, 100, 100, appearance, 0),
            new(4, EntityKind.Player, false, new System.Numerics.Vector3(-20, 0, 0), 0, MotionState.Stand, 100, 100, appearance, 0)
        ], [], [], []));
        for (var frame = 0; frame < 4; frame++)
        {
            manager.Update(0.05f);
        }

        var origin = Vector3.Zero;
        Assert.True(manager.FindAhead(1, origin, facingRight: true, reach: 100, depth: 32, allies: true) == 2
            && manager.FindAhead(1, origin, facingRight: false, reach: 100, depth: 32, allies: true) == 4
            && manager.FindAhead(1, origin, facingRight: true, reach: 50, depth: 32, allies: true) == 0
            && manager.FindAhead(1, origin, facingRight: true, reach: 100, depth: 32, allies: false) == 0,
            "the search ahead took one off the line, behind, out of reach or of the wrong side");
        var behind = new Vector3(-6, 0, 0);
        Assert.True(manager.FindPlayerBumped(1, origin, new Vector3(-8, 0, 0), reach: 16, height: 16) == 4, "walking into a player did not bump it");
        Assert.True(manager.FindPlayerBumped(1, origin, new Vector3(8, 0, 0), reach: 16, height: 16) == 0, "a player out of reach was bumped");
        Assert.True(manager.FindPlayerBumped(1, new Vector3(-14, 0, 0), behind, reach: 16, height: 16) == 0, "walking away from a close player bumped it");
        Assert.True(manager.FindPlayerBumped(1, new Vector3(-21, 40, 0), new Vector3(-20, 40, 0), reach: 16, height: 16) == 0, "a player at another height was bumped");
        Assert.True(manager.FindPlayerBumped(4, origin, new Vector3(-8, 0, 0), reach: 16, height: 16) == 0, "a walk bumped its own player");
        return Task.CompletedTask;
    }

    private static Task ServerHeightAsync()
    {
        // A monster under a bridge: the server keeps it on the lower floor and the client must not lift it.
        var remote = new RemoteModelEntity(new FakeScene(), ActorHandle.None, 1, 0, new Vector3(0, 7, 0), 0, MotionState.Stand, 1);
        var now = 0d;
        for (var frame = 0; frame < 4; frame++)
        {
            now += 50;
            remote.Update(0.05f, now);
        }

        Assert.True(remote.DrawnPosition.Y == 7, "spawn height was replaced");
        remote.SetTarget(now, new Vector3(0.5f, -40, 0), 0, MotionState.Stand, 1);
        for (var frame = 0; frame < 8; frame++)
        {
            now += 50;
            remote.Update(0.05f, now);
        }

        Assert.True(remote.DrawnPosition.Y == -40 && remote.DrawnPosition.X == 0.5f, "remote height did not follow the server sample");
        return Task.CompletedTask;
    }

    private static Task CollisionRosterAsync()
    {
        var composer = new PendingComposer();
        var manager = new RemoteEntityManager(new FakeScene(), composer, composer, new CharacterClassRegistry(), ".");
        var appearance = new PlayerAppearanceInfo("Remote", PlayerClass.Fighter, 0, 0, 0, "", "", 1);
        var origin = new System.Numerics.Vector3(0, 7, 0);
        manager.Apply(new PresenceUpdate(1,
        [
            new(1, EntityKind.Player, true, origin, 0, MotionState.Stand, 100, 100, appearance, 0),
            new(2, EntityKind.Monster, false, origin + new System.Numerics.Vector3(40, 0, 0), 0, MotionState.Stand, 10, 10, null, 1),
            new(3, EntityKind.Npc, false, origin + new System.Numerics.Vector3(0, 0, 40), 0, MotionState.Stand, 1, 1, null, 2),
            new(4, EntityKind.Player, false, origin + new System.Numerics.Vector3(-25, 0, 0), 0, MotionState.Stand, 100, 100, appearance, 0)
        ], [], [], []));
        var neighbours = new List<EntityNeighbour>();
        var nearby = new List<EntityNeighbour>();
        manager.CollectNeighbours(1, neighbours);
        Assert.True(manager.Count == 0 && neighbours.Count == 3, "asset preparation disabled authoritative blockers");
        foreach (var displacement in new[] { new System.Numerics.Vector3(30, 0, 0), new(0, 0, 30), new(-15, 0, 0), new(100, 0, 0) })
        {
            var desired = origin + displacement;
            var bounds = EntityCollision.QueryBounds(origin, desired, EntityCollisionRadii.Player);
            manager.CollectNeighbours(1, nearby, bounds.Center, bounds.Radius);
            var accepted = EntityCollision.Resolve(origin, desired, EntityCollisionRadii.Player, nearby);
            Assert.True(nearby.Count < neighbours.Count && accepted == origin, "bounded movement query missed a blocker or kept distant actors");
            Assert.True(accepted == EntityCollision.Resolve(origin, desired, EntityCollisionRadii.Player, neighbours), "movement broad phase changed collision");
        }

        composer.Ready.TrySetResult();
        manager.Update(0.1f);
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.All(neighbour => neighbour.Position.Y == 7), "presentation leaked into authority collision space");
        manager.Apply(new PresenceUpdate(2, [], [], [2, 3], []));
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.Count == 1, "removed pending entities left phantom blockers");
        manager.Clear();
        manager.CollectNeighbours(1, neighbours);
        Assert.True(neighbours.Count == 0, "field teardown retained collision state");
        return Task.CompletedTask;
    }

    private static Task CursorPickingAsync()
    {
        var viewport = new Viewport(0, 0, 800, 600);
        var camera = new Vector3(0, 0, 10);
        var view = Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, viewport.AspectRatio, 1f, 100f);
        Assert.True(
            CursorPicking.TryCreateRay(viewport, projection, view, camera, new Point(400, 300), out var centerRay),
            "center cursor did not produce a ray");
        Assert.True(centerRay.Position == camera, "cursor ray started on the near plane instead of the camera");
        Assert.True(CursorPicking.IntersectSphere(centerRay, Vector3.Zero, 1f) is not null, "center cursor missed the visible entity");

        Assert.True(
            CursorPicking.TryCreateRay(viewport, projection, view, camera, new Point(20, 20), out var cornerRay),
            "corner cursor did not produce a ray");
        Assert.True(CursorPicking.IntersectSphere(cornerRay, Vector3.Zero, 1f) is null, "off-entity cursor selected the entity");

        var enclosingRay = new Ray(Vector3.Zero, Vector3.Forward);
        Assert.True(CursorPicking.IntersectSphere(enclosingRay, Vector3.Zero, 40f) is null, "camera inside an NPC volume made every cursor direction a hit");
        return Task.CompletedTask;
    }

    private sealed class PendingComposer : IPlayerActorComposer, IModelActorComposer
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Composed { get; private set; }
        public bool TexturesReady { get; set; } = true;
        public Task PrepareAsync(string root, CharacterVisualSet visuals, int face) => Ready.Task;
        public Task PrepareAsync(string root, string model) => Ready.Task;
        public bool AdvanceTextureUpload(string root, string model) => TexturesReady;
        public PlayerActorHandles Compose(string root, CharacterVisualSet visuals, int face, Matrix world, MotionCode motion)
        {
            Composed++;
            return new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        }

        public ComposedModelActor Compose(string root, string model, Matrix world, MotionCode motion)
        {
            Composed++;
            return new ComposedModelActor(ActorHandle.None, null);
        }
    }

    private sealed class FakeScene : IActorScene
    {
        public int Restarts { get; private set; }
        public int Commands { get; private set; }
        public float LastRate { get; private set; }
        public List<int> ExactMotionIndices { get; } = [];
        public ActorHandle AddActor(PristonActorInstance instance) => ActorHandle.None;
        public void SetActorWorld(ActorHandle handle, Matrix world)
        {
        }
        public void SetActorMotion(ActorHandle handle, MotionCode motion) => Commands++;
        public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
        {
            ExactMotionIndices.Add(motionIndex);
            Commands++;
        }
        public void RestartActorMotion(ActorHandle handle, MotionCode motion)
        {
            Restarts++;
            Commands++;
        }

        public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => LastRate = rateFactor;
        public void RemoveActor(ActorHandle handle)
        {
        }
    }
}
