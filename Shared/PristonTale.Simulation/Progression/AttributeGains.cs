using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Progression;

// What attribute points planned in the status window will add, before they are confirmed: the difference the derived
// stats' formulas (DerivedStats.Compute) and the weapon's attribute damage (WeaponDamage.AttributeScaled) make between the
// attributes now and with the points. The equipment's own values do not change with the points and are left out; the
// percent modifiers of skills and buffs are not applied, so a value they scale may differ by a point or so.
public readonly record struct AttributeGains(
    int DamageMin,
    int DamageMax,
    int AttackRating,
    int Defence,
    int Absorption,
    int MaxLife,
    int MaxMana,
    int MaxStamina,
    int MaxWeight,
    int MoveSpeed)
{
    // weapon: the worn weapon's kind, or null bare-handed; wornDamageMin and wornDamageMax: the damage range of the
    // usable worn items together (EquipmentTotals), which the weapon's damage attribute grows.
    public static AttributeGains Of(CharacterFunctionProfile function, int level, CharacterAttributes now, CharacterAttributes planned,
        ItemKind? weapon, int wornDamageMin, int wornDamageMax)
    {
        var before = DerivedStats.Compute(now, level, function);
        var after = DerivedStats.Compute(planned, level, function);
        (int Min, int Max) damage = weapon is { } kind
            ? Difference(WeaponDamage.AttributeScaled(now, function, kind, wornDamageMin, wornDamageMax),
                WeaponDamage.AttributeScaled(planned, function, kind, wornDamageMin, wornDamageMax))
            : (after.AttackDamageMin - before.AttackDamageMin, after.AttackDamageMax - before.AttackDamageMax);
        return new AttributeGains(
            damage.Min,
            damage.Max,
            after.AttackRating - before.AttackRating,
            after.Defence - before.Defence,
            after.Absorption - before.Absorption,
            after.MaxLife - before.MaxLife,
            after.MaxMana - before.MaxMana,
            after.MaxStamina - before.MaxStamina,
            after.MaxWeight - before.MaxWeight,
            after.MoveSpeed - before.MoveSpeed);

        static (int Min, int Max) Difference((int Min, int Max) before, (int Min, int Max) after) => (after.Min - before.Min, after.Max - before.Max);
    }
}
