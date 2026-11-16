using PristonTale.Contracts.Units;
using PristonTale.Contracts.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Gameplay.Combat;

public sealed record FreeHuntSettings
{
    public const int UnitsPerStep = 64;
    public const int DefaultRadius = 640;
    public const int DefaultLootRadius = 320;
    public const int MaximumRadius = 30 * UnitsPerStep;
    public int Radius { get; init; } = DefaultRadius;
    public bool ReturnToStart { get; init; } = true;
    public bool RelocateWhenBlocked { get; init; } = true;
    public bool BreakCombatToReturn { get; init; }
    public string OnlyMonsters { get; init; } = "";
    public string IgnoredMonsters { get; init; } = "";
    public int MinimumLevel { get; init; }
    public int MaximumLevel { get; init; }
    public bool StrictMonsterFilter { get; init; }
    public bool AvoidClaimed { get; init; } = true;
    public bool IncludePartyTargets { get; init; }
    public bool PrioritizeQuest { get; init; } = true;
    public bool KeepTarget { get; init; } = true;
    public bool PrioritizeRanged { get; init; }
    public bool PrioritizeBoss { get; init; }
    public int PursuitPercent { get; init; } = 20;
    public int TargetTimeout { get; init; } = 12;
    public int IgnoreSeconds { get; init; } = 30;
    public bool ShowStatus { get; init; } = true;
    public bool ShareAccount { get; init; }

    public uint[] Skills { get; init; } = [];
    public Dictionary<uint, int> SkillManaFloor { get; init; } = [];
    public bool BasicAttacks { get; init; } = true;
    public bool PreferArea { get; init; } = true;
    public int AreaMinimum { get; init; } = 3;
    public int AreaRadius { get; init; }
    public int AreaCastRange { get; init; } = 160;
    public bool UseCrystals { get; init; }
    public string Crystal { get; init; } = "";
    public int SummonCount { get; init; } = 1;
    public bool CrystalInCombat { get; init; } = true;
    public CrystalPetTargeting CompanionTarget { get; init; }
    public bool UseForce { get; init; }
    public string ForceKey { get; init; } = "";
    public bool WeakForceFirst { get; init; }
    public bool ForceInCombat { get; init; } = true;
    public bool UsePremiums { get; init; }
    public bool SwapShieldForBuff { get; init; } = true;

    public PotionHuntRule HP { get; init; } = new(true, 55, "");
    public PotionHuntRule MP { get; init; } = new(true, 35, "");
    public PotionHuntRule RES { get; init; } = new(true, 20, "");
    public bool AdaptivePotion { get; init; } = true;
    public bool RefillBelt { get; init; } = true;
    public int SamePotionMs { get; init; } = 800;
    public int AnyPotionMs { get; init; } = 600;

    public bool Loot { get; init; } = true;
    public int LootRadius { get; init; } = DefaultLootRadius;
    public LootHuntMode LootMode { get; init; }
    public LootHuntCategory Excluded { get; init; }
    public bool HasExpandedLootCategories { get; init; }
    public string AlwaysLoot { get; init; } = "";
    public string NeverLoot { get; init; } = "";
    public int MinimumItemLevel { get; init; }
    public int MaximumItemLevel { get; init; }
    public Dictionary<LootHuntCategory, ItemLevelRange> LevelByCategory { get; init; } = [];
    public bool PrioritizeOwnSpec { get; init; }
    public bool OnlyOwnSpec { get; init; }
    public int MaxLifePotions { get; init; }
    public int MaxManaPotions { get; init; }
    public int MaxStaminaPotions { get; init; }
    public string ExcludedPotions { get; init; } = "";
    public bool IgnoreOwnDrops { get; init; } = true;
    public bool ReturnAfterLoot { get; init; }
    public int LootAfterSeconds { get; init; }

    public int CriticalHp { get; init; } = 20;
    public HuntSafety CriticalAction { get; init; } = HuntSafety.Town;
    public HuntSafety NoHealthPotion { get; init; }
    public HuntSafety FullBag { get; init; }
    public bool RestartInTown { get; init; }
    public bool TownAfterDeaths { get; init; } = true;
    public bool TownWhenLowScrolls { get; init; }
    public bool UseCoreWhenOutOfPotions { get; init; }
    public bool StopOutsideParty { get; init; }
    public bool PauseForManual { get; init; } = true;
    public int ResumeMs { get; init; } = 1500;
    public bool CountOnlyHunting { get; init; } = true;

    public static float WorldRadius(int units) => Math.Clamp(units, 0, MaximumRadius) * (GameUnits.WorldUnitsPerMetre / UnitsPerStep);

    public static bool Matches(string value, string terms)
    {
        if (string.IsNullOrEmpty(terms) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        var rest = terms.AsSpan();
        while (!rest.IsEmpty)
        {
            var separator = rest.IndexOf(',');
            var term = (separator < 0 ? rest : rest[..separator]).Trim();
            if (!term.IsEmpty && value.AsSpan().Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            rest = separator < 0 ? default : rest[(separator + 1)..];
        }

        return false;
    }

    public bool AcceptsMonster(string name, int level, bool aggressor) =>
        (aggressor && !StrictMonsterFilter)
        || (string.IsNullOrWhiteSpace(OnlyMonsters) || Matches(name, OnlyMonsters)) && !Matches(name, IgnoredMonsters)
            && level >= MinimumLevel && (MaximumLevel == 0 || level <= MaximumLevel);

    public FreeHuntSettings UpgradeLootCategories()
    {
        if (HasExpandedLootCategories)
        {
            return this;
        }

        const LootHuntCategory formerlyOther = LootHuntCategory.Force | LootHuntCategory.Materials | LootHuntCategory.Potions
            | LootHuntCategory.Runes | LootHuntCategory.Travel | LootHuntCategory.Premium | LootHuntCategory.Castle
            | LootHuntCategory.Masteries | LootHuntCategory.Events | LootHuntCategory.OtherUsable;
        var excluded = (Excluded & LootHuntCategory.Other) != 0 ? Excluded | formerlyOther : Excluded;
        return this with { Excluded = excluded, HasExpandedLootCategories = true };
    }

    public bool AcceptsDrop(GroundDropView drop, OwnerState owner, uint spec) => LootPriority(drop, owner, spec) != 0;

    public int LootPriority(GroundDropView drop, OwnerState owner, uint spec)
    {
        if (QuestItems.IsQuestDrop(drop.ItemCode))
        {
            return 4;
        }

        var category = Category(drop.CatalogKey, drop.ItemCode, drop.Gold);
        var family = PotionDefinition.FamilyOf(drop.CatalogKey);
        if (IgnoreOwnDrops && drop.ThrownByReceiver || MatchesLoot(drop, category, family, NeverLoot))
        {
            return 0;
        }

        var limit = family switch
        {
            PotionResource.Life => MaxLifePotions,
            PotionResource.Mana => MaxManaPotions,
            PotionResource.Stamina => MaxStaminaPotions,
            _ => 0
        };
        if (limit > 0 && (long)CountPotions(owner.Items, family) + drop.Amount > limit)
        {
            return 0;
        }
        if (family != PotionResource.None && MatchesLoot(drop, category, family, ExcludedPotions))
        {
            return 0;
        }

        if (MatchesLoot(drop, category, family, AlwaysLoot))
        {
            return 3;
        }

        var ownSpec = (drop.Specialization & spec) != 0;
        if (IsEquipment(category))
        {
            var limits = LevelByCategory.GetValueOrDefault(category, new ItemLevelRange(MinimumItemLevel, MaximumItemLevel));
            if (OnlyOwnSpec && !ownSpec || drop.RequiredLevel > 0
                && (drop.RequiredLevel < limits.Minimum || limits.Maximum > 0 && drop.RequiredLevel > limits.Maximum))
            {
                return 0;
            }
        }

        var accepted = (Excluded & category) == 0 && (LootMode == LootHuntMode.All
            || LootMode == LootHuntMode.Gold && drop.Gold
            || LootMode == LootHuntMode.GoldAndPotions && (drop.Gold || family != PotionResource.None));
        return accepted ? (PrioritizeOwnSpec && ownSpec ? 2 : 1) : 0;
    }

    private static bool MatchesLoot(GroundDropView drop, LootHuntCategory category, PotionResource family, string terms)
    {
        var potionGroup = family switch
        {
            PotionResource.Life => "@HP",
            PotionResource.Mana => "@MP",
            PotionResource.Stamina => "@RES",
            _ => ""
        };
        foreach (var range in terms.AsSpan().Split(','))
        {
            var term = terms.AsSpan()[range].Trim();
            if (term.IsEmpty)
            {
                continue;
            }

            var matches = term[0] switch
            {
                '@' => term.Equals(GroupKey(category), StringComparison.OrdinalIgnoreCase) || term.Equals(potionGroup, StringComparison.OrdinalIgnoreCase),
                '#' => term[1..].Equals(drop.CatalogKey, StringComparison.OrdinalIgnoreCase),
                _ => drop.Name.AsSpan().Contains(term, StringComparison.OrdinalIgnoreCase) || drop.CatalogKey.AsSpan().Contains(term, StringComparison.OrdinalIgnoreCase)
            };
            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<LootHuntCategory, string> Groups = Enum.GetValues<LootHuntCategory>()
        .ToDictionary(value => value, value => "@" + value);

    public static string GroupKey(LootHuntCategory category) => Groups.GetValueOrDefault(category, "@Other");

    public static bool IsEquipment(LootHuntCategory category) =>
        (category & (LootHuntCategory.Weapons | LootHuntCategory.Armor | LootHuntCategory.Shields | LootHuntCategory.Boots |
            LootHuntCategory.Gloves | LootHuntCategory.Bracelets | LootHuntCategory.Amulets | LootHuntCategory.Rings)) != 0;

    private static int CountPotions(IReadOnlyList<OwnedItemView> items, PotionResource family)
    {
        var count = 0;
        foreach (var item in items)
        {
            if ((item.InBag || item.BeltBox >= 0) && PotionDefinition.FamilyOf(item.CatalogKey) == family)
            {
                count += item.Count;
            }
        }

        return count;
    }

    public static LootHuntCategory Category(string catalogKey, uint code, bool gold)
    {
        if (gold)
        {
            return LootHuntCategory.Gold;
        }
        if (QuestItems.IsQuestDrop(code))
        {
            return LootHuntCategory.Quest;
        }
        if (UsableItemCatalog.TryGet(catalogKey, out var rule))
        {
            return rule.Action switch
            {
                UsableItemAction.MonsterCrystal => LootHuntCategory.Crystals,
                UsableItemAction.ForceOrb => LootHuntCategory.Force,
                UsableItemAction.ReturnToTown or UsableItemAction.TeleportCore or UsableItemAction.UnionCore => LootHuntCategory.Travel,
                UsableItemAction.PremiumTimers or UsableItemAction.GravityScroll or UsableItemAction.MobileShop => LootHuntCategory.Premium,
                UsableItemAction.CastleItem => LootHuntCategory.Castle,
                UsableItemAction.AgingMastery or UsableItemAction.SkillMastery => LootHuntCategory.Masteries,
                UsableItemAction.Food or UsableItemAction.GiftBox or UsableItemAction.PuzzleBag or UsableItemAction.Chocolate
                    or UsableItemAction.Firecracker or UsableItemAction.SoccerHead => LootHuntCategory.Events,
                _ => LootHuntCategory.OtherUsable
            };
        }

        if (CraftMaterials.Colour(CraftMaterials.RuneA, catalogKey) > 0 || CraftMaterials.Colour(CraftMaterials.RuneB, catalogKey) > 0)
        {
            return LootHuntCategory.Runes;
        }
        if (CraftMaterials.Colour(CraftMaterials.Ore, catalogKey) > 0 || CraftMaterials.Colour(CraftMaterials.Crystal, catalogKey) > 0)
        {
            return LootHuntCategory.Materials;
        }

        return ItemKinds.Classify(catalogKey) switch
        {
            ItemKind.Sword or ItemKind.Axe or ItemKind.Hammer or ItemKind.Claw or ItemKind.Pole or ItemKind.Bow or
                ItemKind.ThrowingWeapon or ItemKind.Staff or ItemKind.Dagger or ItemKind.Phantom or ItemKind.Vambrace => LootHuntCategory.Weapons,
            ItemKind.Armor => LootHuntCategory.Armor,
            ItemKind.Shield => LootHuntCategory.Shields,
            ItemKind.Boots => LootHuntCategory.Boots,
            ItemKind.Gloves => LootHuntCategory.Gloves,
            ItemKind.Bracelet or ItemKind.Orb => LootHuntCategory.Bracelets,
            ItemKind.Amulet => LootHuntCategory.Amulets,
            ItemKind.Ring => LootHuntCategory.Rings,
            ItemKind.Sheltom => LootHuntCategory.Sheltoms,
            ItemKind.ForceOrb => LootHuntCategory.Force,
            ItemKind.Potion => LootHuntCategory.Potions,
            ItemKind.Material => LootHuntCategory.Materials,
            ItemKind.Quest => LootHuntCategory.Quest,
            _ => LootHuntCategory.Other
        };
    }
}

public sealed record PotionHuntRule(bool Enabled, int Threshold, string Key);
public readonly record struct ItemLevelRange(int Minimum, int Maximum);
public enum HuntSafety { Continue, Pause, Town }
public enum LootHuntMode { All, Gold, GoldAndPotions, ListedOnly }

[Flags]
public enum LootHuntCategory
{
    Gold = 1, Weapons = 2, Armor = 4, Shields = 8, Boots = 16, Gloves = 32, Bracelets = 64,
    Amulets = 128, Rings = 256, Sheltoms = 512, Force = 1024, Crystals = 2048,
    Materials = 4096, Potions = 8192, Other = 16384, Quest = 32768, Runes = 65536, Travel = 131072, Premium = 262144,
    Castle = 524288, Masteries = 1048576, Events = 2097152, OtherUsable = 4194304
}
