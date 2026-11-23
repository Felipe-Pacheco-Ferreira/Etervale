using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Combat;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

internal static class FreeHuntTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("free auto-hunt applies monster and loot filters in their specified precedence", FreeHuntFiltersAsync);
        yield return ("free auto-hunt categorizes catalog drops and protects potion quotas", FreeHuntLootCatalogAsync);
        yield return ("free auto-hunt chooses the smallest guaranteed potion and respects a preferred choice", FreeHuntPotionsAsync);
        yield return ("free auto-hunt profile transfer preserves filters and rejects malformed codes", FreeHuntProfileAsync);
        yield return ("a shared hunt profile reaches the account's characters, each keeping its own skills, and the panel keeps its sharing",
            ProfileStoreAsync);
        yield return ("the hunt's log and totals are kept and given back, and its stops are logged with their reason", HuntRecordAsync);
        yield return ("the hunt panel's buttons name their action, never a number", HuntActionsAsync);
    }

    private static Task ProfileStoreAsync()
    {
        const string scope = "wss://play.example.com/HUNTER";
        var shared = new FreeHuntSettings { Radius = 1280, ShareAccount = true, Skills = [12], SkillManaFloor = new Dictionary<uint, int> { [12] = 40 } };
        var profiles = FreeHuntProfileStore.Save(new Dictionary<string, string>(), "Ayla", scope, shared);
        Assert.True(profiles.Count == 2 && !profiles.Keys.Any(key => key.Contains("HUNTER", StringComparison.OrdinalIgnoreCase)),
            "the account was kept by its name or a profile is missing");
        profiles = FreeHuntProfileStore.Save(profiles, "Brann", "", new FreeHuntSettings { Skills = [34] });
        var brann = FreeHuntProfileStore.Load(profiles, "Brann", scope);
        Assert.True(brann.Radius == 1280 && brann.ShareAccount && brann.Skills.SequenceEqual([34u]) && brann.SkillManaFloor.Count == 0,
            "another character of the account did not take the shared options with its own skills");
        var ayla = FreeHuntProfileStore.Load(profiles, "Ayla", scope);
        Assert.True(ayla.Skills.SequenceEqual([12u]) && ayla.SkillManaFloor[12] == 40, "a character lost its skills to the shared profile");
        profiles = FreeHuntProfileStore.Save(profiles, "Ayla", scope, ayla with { ShareAccount = false, Radius = 640 });
        Assert.True(!FreeHuntProfileStore.Load(profiles, "Brann", scope).ShareAccount && FreeHuntProfileStore.Load(profiles, "Brann", scope).Radius == 640,
            "turning the sharing off left the account's profile behind");
        Assert.True(FreeHuntProfile.TryRead(System.Text.Json.JsonSerializer.Serialize(shared), out var read) && read.ShareAccount && read.Radius == 1280,
            "the panel's options lost their sharing");
        Assert.True(!FreeHuntProfile.TryRead("{\"Radius\":1}", out _) && !FreeHuntProfile.TryRead("not json", out _), "invalid panel options were read");
        return Task.CompletedTask;
    }

    private static Task HuntRecordAsync()
    {
        var session = new Gameplay.World.FreeHuntSession(null!);
        session.SetEnabled(true);
        session.SetEnabled(false, Gameplay.World.HuntStopReason.FullBag);
        var entries = session.LogEntries.ToArray();
        Assert.True(entries.Length == 2 && entries[0].Kind == Gameplay.World.HuntLogKind.Started
            && entries[1] is { Kind: Gameplay.World.HuntLogKind.Stopped, Reason: Gameplay.World.HuntStopReason.FullBag }, "the start and stop were not logged");
        var saved = new Gameplay.World.HuntRecord(Gameplay.World.HuntTotals.Empty with { Kills = 7, Items = [new("PL101", "Potion", 3)] },
            ["12:00:00|Death||Hopy", "12:01:00|Joined|Manual|Ayla|x", "bad"]);
        var restored = new Gameplay.World.FreeHuntSession(null!);
        restored.Restore(saved);
        Assert.True(restored.Totals.Kills == 7 && restored.Totals.Items.Single().Count == 3, "the saved totals were not given back");
        Assert.True(restored.LogEntries.Single() is { Kind: Gameplay.World.HuntLogKind.Joined, Name: "Ayla|x" }, "a broken log line was read or a good one lost");
        var again = restored.Record();
        Assert.True(again.Log.SequenceEqual(["12:01:00|Joined|Manual|Ayla|x"]) && again.Totals.Kills == 7, "the record did not round-trip");
        return Task.CompletedTask;
    }

    private static Task HuntActionsAsync()
    {
        Assert.True(Gameplay.UI.GameplayHud.TryReadHuntAction("touch-hunt:Toggle", out var kind) && kind == Gameplay.UI.TouchHuntAction.Toggle,
            "the toggle was not read");
        Assert.True(Gameplay.UI.GameplayHud.TryReadHuntAction("touch-hunt:previewloot", out kind) && kind == Gameplay.UI.TouchHuntAction.PreviewLoot,
            "an action was read by its case");
        Assert.True(!Gameplay.UI.GameplayHud.TryReadHuntAction("touch-hunt:3", out _) && !Gameplay.UI.GameplayHud.TryReadHuntAction("touch-hunt:Fly", out _)
            && !Gameplay.UI.GameplayHud.TryReadHuntAction("touch-npc:yes", out _), "a number, an unknown action or a foreign one was read");
        return Task.CompletedTask;
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
            RequiredLevel = 130, SpecJob = spec
        };
        var limits = filters with { OnlyOwnSpec = true, MinimumItemLevel = 100, MaximumItemLevel = 140 };
        Assert.True(limits.AcceptsDrop(drop, owner, spec)
            && !limits.AcceptsDrop(drop with { RequiredLevel = 90 }, owner, spec)
            && !limits.AcceptsDrop(drop with { SpecJob = (uint)JobBitCode.Fighter }, owner, spec)
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
        var weapon = priority.LootPriority(Ground("WS201", "Sword") with { SpecJob = spec }, owner, spec);
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
}
