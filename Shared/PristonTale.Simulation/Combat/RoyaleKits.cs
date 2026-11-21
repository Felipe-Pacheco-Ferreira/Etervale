using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Combat;

// The Battle Royale's stand-ins (RoyaleRules): every player fights as a character of its class at KitLevel, its status
// points spent as its class's line of the table below (Health takes what the others leave), wearing the kit the rule
// picks from the item catalog: for each slot, the best item (highest required level, then price) of RoyaleRules.KitLevel
// or below that the stand-in can wear, perfect and specialised for the class when the item can be and it reaches the
// specialised requirements; the weapon one the class favours, else one of its beginner weapon's kind, else any; the
// rest by the slot alone. Rare, unpriced and quest items are left out. The stand-in keeps the player's tier
// and skills; it is marked a stand-in, so nothing ever stores it (PlayerCharacter.IsStandIn).
public static class RoyaleKits
{
    // Strength, Spirit, Talent and Agility at KitLevel, by class.
    private static readonly Dictionary<PlayerClass, (int Strength, int Spirit, int Talent, int Agility)> Spending = new()
    {
        [PlayerClass.Fighter] = (208, 40, 90, 77),
        [PlayerClass.Mechanician] = (208, 40, 90, 80),
        [PlayerClass.Archer] = (80, 40, 80, 180),
        [PlayerClass.Pikeman] = (208, 40, 90, 77),
        [PlayerClass.Atalanta] = (80, 40, 90, 150),
        [PlayerClass.Knight] = (208, 40, 95, 77),
        [PlayerClass.Magician] = (70, 214, 90, 40),
        [PlayerClass.Priestess] = (70, 214, 90, 40),
        [PlayerClass.Assassin] = (208, 40, 90, 77),
        [PlayerClass.Shaman] = (70, 214, 90, 40),
        [PlayerClass.MartialArtist] = (208, 40, 90, 77)
    };

    private static readonly EquipmentSlot[] WornSlots =
    [
        EquipmentSlot.Armor, EquipmentSlot.Gloves, EquipmentSlot.Boots, EquipmentSlot.Bracelet, EquipmentSlot.Amulet
    ];

    // The attributes a class's stand-in has: its base plus every status point of KitLevel, as the table spends them.
    public static CharacterAttributes Attributes(CharacterClassDef definition)
    {
        var total = (int)definition.BaseAttributes.Total + (RoyaleRules.KitLevel - 1) * PlayerCharacter.StatusPointsPerLevel;
        var (strength, spirit, talent, agility) = Spending[definition.Class];
        var basis = definition.BaseAttributes;
        strength = Math.Max(strength, basis.Strength);
        spirit = Math.Max(spirit, basis.Spirit);
        talent = Math.Max(talent, basis.Talent);
        agility = Math.Max(agility, basis.Agility);
        return new CharacterAttributes(strength, spirit, talent, agility, Math.Max(basis.Health, total - strength - spirit - talent - agility));
    }

    // The player's stand-in: its class at KitLevel with the table's attributes, the player's tier and skill ranks, a copy
    // of its class's kit (Kit, by EquipmentSlot) worn and its pools full.
    public static PlayerCharacter StandIn(PlayerCharacter player, ItemInstance[] kit)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(kit);
        var standIn = Bare(player.Class, player.Name);
        standIn.RestoreProgress(ExperienceAt(RoyaleRules.KitLevel), 0, 0, player.Tier, 0, 0);
        foreach (var skill in player.Skills.Ranks)
        {
            standIn.Skills.Restore(skill.Key, skill.Value, player.Skills.UsesOf(skill.Key));
        }

        for (var slot = 0; slot < kit.Length; slot++)
        {
            if (kit[slot] is { } item)
            {
                standIn.Equip((EquipmentSlot)slot, item.Copy(1));
            }
        }

        standIn.Life = standIn.MaxLife;
        standIn.Mana = standIn.MaxMana;
        standIn.Stamina = standIn.MaxStamina;
        return standIn;
    }

    private static PlayerCharacter Bare(CharacterClass characterClass, string name) =>
        new(characterClass, name, RoyaleRules.KitLevel, Attributes(characterClass.Definition)) { IsStandIn = true };

    // A class's kit, by EquipmentSlot (null for an empty slot), picked once per event from the catalog.
    public static ItemInstance[] Kit(CharacterClass characterClass, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(characterClass);
        ArgumentNullException.ThrowIfNull(items);
        var standIn = Bare(characterClass, characterClass.Definition.Name);
        var profile = ClassProfiles.Of(characterClass.Class);
        var beginnerKind = ItemEquipmentMetadata.KindOf(profile.BeginnerWeapon);
        if ((Best(standIn, items, EquipmentSlot.Weapon, profile.Favours)
            ?? Best(standIn, items, EquipmentSlot.Weapon, item => item.Definition.Kind == beginnerKind)
            ?? Best(standIn, items, EquipmentSlot.Weapon, null)) is { } weapon)
        {
            standIn.Equip(EquipmentSlot.Weapon, weapon);
        }

        if (Best(standIn, items, EquipmentSlot.OffHand, null) is { } offHand && standIn.CanEquip(EquipmentSlot.OffHand, offHand))
        {
            standIn.Equip(EquipmentSlot.OffHand, offHand);
        }

        foreach (var slot in WornSlots)
        {
            if (Best(standIn, items, slot, null) is { } item)
            {
                standIn.Equip(slot, item);
            }
        }

        if (Best(standIn, items, EquipmentSlot.RingLeft, null) is { } ring)
        {
            standIn.Equip(EquipmentSlot.RingLeft, ring);
            standIn.Equip(EquipmentSlot.RingRight, ring.Copy(1));
        }

        var kit = new ItemInstance[EquipmentSlots.Count];
        for (var slot = 0; slot < kit.Length; slot++)
        {
            kit[slot] = standIn.GetEquipped((EquipmentSlot)slot);
        }

        return kit;
    }

    private static ulong ExperienceAt(int level) => Progression.ExperienceTable.ForLevel(Math.Min(level, Progression.ExperienceTable.MaximumLevel));

    private static ItemInstance Best(PlayerCharacter standIn, ItemCatalog items, EquipmentSlot slot, Func<ItemInstance, bool> favoured)
    {
        var job = (uint)standIn.Definition.JobBitCode;
        ItemInstance best = null;
        foreach (var definition in items.All)
        {
            if (ItemEquipmentMetadata.SlotOf(definition.Kind) != slot || definition.RequiredLevel > RoyaleRules.KitLevel || definition.Price <= 0
                || definition.IsRare || definition.UniqueItem != 0
                || best is not null && (definition.RequiredLevel < best.Definition.RequiredLevel
                    || definition.RequiredLevel == best.Definition.RequiredLevel && definition.Price <= best.Definition.Price))
            {
                continue;
            }

            // A class's specialisation moves the requirements: the plain copy when the specialised one is out of reach.
            var item = ItemRoller.CanTakeSpec(definition, job) ? ItemRoller.CreatePerfect(definition, 1, job) : null;
            if (item is null || !standIn.CanWear(slot, item))
            {
                item = ItemRoller.CreatePerfect(definition, 1, 0u);
            }

            if (item is not null && standIn.CanWear(slot, item) && (favoured is null || favoured(item)))
            {
                best = item;
            }
        }

        return best;
    }
}
