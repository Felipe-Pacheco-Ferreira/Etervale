using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Combat;

public static class WeaponDamage
{
    // The attribute a weapon's damage grows with: spirit for a caster's staff or phantom, agility for bows and throwing
    // weapons, strength for the rest (and for no known weapon).
    public static CharacterAttribute DamageAttribute(ItemKind? weapon, CharacterFunctionProfile function) => weapon switch
    {
        ItemKind.Staff or ItemKind.Phantom when function.MagicDamageFunction > 0 => CharacterAttribute.Spirit,
        ItemKind.Bow or ItemKind.ThrowingWeapon => CharacterAttribute.Agility,
        _ => CharacterAttribute.Strength
    };

    public static (int Min, int Max) Compute(PlayerCharacter character)
    {
        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        if (weapon is null)
        {
            // Bare-hand damage already includes the modifiers through the derived stats.
            return (character.Derived.AttackDamageMin, character.Derived.AttackDamageMax);
        }

        // Every usable worn item adds its damage range; a held weapon the character cannot use adds nothing.
        var items = character.EquipmentTotals;
        var scaled = AttributeScaled(character.EffectiveAttributes, character.Definition.FunctionProfile, weapon.Definition.Kind, items.DamageMin,
            items.DamageMax);
        var favoured = ClassProfiles.Of(character.Definition.Class).Favours(weapon);
        var jobDamage = favoured ? character.Level / 6 : 0;
        var mastery = PassiveSkills.WeaponBonus(character, weapon);
        var passive = PassiveSkillEffects.WeaponBonus(character, weapon);
        mastery = (mastery.Min + passive.Min, mastery.Max + passive.Max);
        var minimum = 2 + scaled.Min + jobDamage + (items.DamageMin + items.DamageMax) / 16;
        var maximum = 4 + scaled.Max + jobDamage + items.LevelDamage;
        return ModifierRules.ApplyDamage(character.Effects.Totals, minimum + mastery.Min, maximum + mastery.Max);
    }

    // The part of a weapon's damage the attributes make: the worn items' damage range grown by the weapon's damage
    // attribute, plus the secondary attributes' share.
    public static (int Min, int Max) AttributeScaled(CharacterAttributes attributes, CharacterFunctionProfile function, ItemKind weapon,
        int damageMin, int damageMax)
    {
        int primary;
        int divisor;
        int secondary;
        var attribute = DamageAttribute(weapon, function);
        if (attribute == CharacterAttribute.Spirit)
        {
            primary = attributes.Spirit;
            divisor = function.MagicDamageFunction == 1 ? 150 : 170;
            secondary = attributes.Talent / 30;
        }
        else if (attribute == CharacterAttribute.Agility)
        {
            primary = attributes.Agility;
            divisor = function.RangedDamageFunction == 1 ? 130 : 190;
            secondary = (attributes.Talent + attributes.Strength) / (function.RangedDamageFunction == 1 ? 40 : 50);
        }
        else
        {
            primary = attributes.Strength;
            divisor = function.MeleeDamageFunction switch
            {
                1 => 130,
                2 => 150,
                _ => 190
            };
            var secondaryDivisor = function.MeleeDamageFunction switch
            {
                1 => 40,
                2 => 45,
                _ => 50
            };
            secondary = (attributes.Talent + attributes.Agility) / secondaryDivisor;
        }

        return (damageMin * (primary + divisor) / divisor + secondary, damageMax * (primary + divisor) / divisor + secondary);
    }

    // The attack range of a single-target hit (a normal attack, a weapon skill's hit) and of the status window: the
    // attack range raised by HitDamageMin and HitDamageMax.
    public static (int Min, int Max) ComputeForHit(PlayerCharacter character)
    {
        var (min, max) = Compute(character);
        var totals = character.Effects.Totals;
        min = totals.Apply(StatKind.HitDamageMin, min);
        return (min, Math.Max(min, totals.Apply(StatKind.HitDamageMax, max)));
    }

    // A percent of the weapon's own damage range, each end truncated (the percent applied before the multiplication).
    public static (int Min, int Max) PercentOfWeapon(ItemInstance weapon, int percent)
    {
        var stats = weapon.TotalStats;
        return ((int)(percent / 100f * stats.DamageMin), (int)(percent / 100f * stats.DamageMax));
    }
}
