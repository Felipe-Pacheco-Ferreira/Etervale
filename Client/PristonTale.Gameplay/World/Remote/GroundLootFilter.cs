using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Gameplay.World.Remote;

internal static class GroundLootFilter
{
    public static readonly (string Portuguese, string English)[] Labels =
    [
        ("Poções de vida", "Life potions"), ("Poções de mana", "Mana potions"), ("Poções de stamina", "Stamina potions"),
        ("Armaduras e robes", "Armor and robes"), ("Escudos", "Shields"), ("Botas", "Boots"), ("Luvas", "Gloves"),
        ("Braceletes e orbs", "Bracelets and orbs"), ("Amuletos", "Amulets"), ("Anéis", "Rings"), ("Sheltoms", "Sheltoms"),
        ("Force Orbs", "Force orbs"), ("Outros itens", "Other items"), ("Cristais de invocação", "Summoning crystals"), ("Cores", "Cores")
    ];

    public static bool IsVisible(ClientSettings settings, GroundDropView drop)
    {
        if (settings.DisableGroundLootFilter)
        {
            return true;
        }

        if (drop.Gold)
        {
            return !settings.HideGroundGold;
        }

        var key = drop.CatalogKey;
        if (QuestItemBox.Keeps(drop.ItemCode))
        {
            return true;
        }

        if (settings.AlwaysShowGroundNames is { } names && drop.Name is { } name)
        {
            foreach (var pattern in names)
            {
                if (!string.IsNullOrWhiteSpace(pattern) && name.AsSpan().Contains(pattern.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (drop.HasEquipmentDetails)
        {
            if (drop.Rare && settings.AlwaysShowRareGroundEquipment)
            {
                return true;
            }

            if (drop.RequiredLevel < settings.GroundMinimumEquipmentLevel ||
                (settings.GroundMaximumEquipmentLevel > 0 && drop.RequiredLevel > settings.GroundMaximumEquipmentLevel) ||
                (drop.SpecJob & settings.HiddenGroundSpecJobs) != 0 ||
                (drop.SpecJob == 0 && settings.HideGroundEquipmentWithoutSpec))
            {
                return false;
            }
        }

        var potion = PotionDefinition.FamilyOf(key);
        if (potion != PotionResource.None)
        {
            var category = potion switch { PotionResource.Life => 0, PotionResource.Mana => 1, _ => 2 };
            return !settings.HideGroundPotions && !Hidden(settings, category);
        }

        if (ItemEquipmentMetadata.TryGet(key, out var kind, out var slot))
        {
            if (slot == PristonTale.Simulation.Characters.EquipmentSlot.Weapon)
            {
                return !settings.HideGroundWeapons;
            }

            return !Hidden(settings, kind switch
            {
                ItemKind.Armor => 3, ItemKind.Shield => 4, ItemKind.Boots => 5, ItemKind.Gloves => 6,
                ItemKind.Bracelet or ItemKind.Orb => 7, ItemKind.Amulet => 8, ItemKind.Ring => 9, ItemKind.Sheltom => 10,
                _ => 12
            });
        }

        if (UsableItemCatalog.TryGet(key, out var usable))
        {
            if (usable.Action == UsableItemAction.MonsterCrystal)
            {
                return !Hidden(settings, 13);
            }

            if (usable.Action is UsableItemAction.TeleportCore or UsableItemAction.UnionCore)
            {
                return !Hidden(settings, 14);
            }
        }

        var forceOrb = ForceOrbs.TryGet(key, out _);
        return !Hidden(settings, forceOrb ? 11 : 12);
    }

    public static bool Hidden(ClientSettings settings, int category) => (settings.HiddenGroundCategories & (1UL << category)) != 0;
}
