namespace PristonTale.Simulation.Social.Castle;

// How the server runs Bless Castle (a server setting): no sieges, a weekly siege, or the open league (sieges one after
// another for CastleRules.LeagueMinutes, each restarting CastleRules.LeagueRestartMinutes after the last ended).
public enum CastleMode : byte
{
    Off = 0,
    Weekly = 1,
    League = 2
}

// Peace, a siege running, or a siege over and closing (CastleRules.OverSeconds) before peace.
public enum SiegeState : byte
{
    Peace = 0,
    Running = 1,
    Over = 3
}

// The owner clan's castle skill: absorption, damage or evasion.
public enum CastleClanSkill : byte
{
    None = 0,
    Absorb = 1,
    Damage = 2,
    Evasion = 3
}

// Bless Castle's rules. A siege lasts SiegeMinutes; the clan whose members dealt the most damage to the Tower of
// Valhalla when it falls takes the castle, and the owner keeps it when the time runs out. Notices go out an hour and
// half an hour before a weekly siege. The owner's tax rate and clan skill take effect each day at TaxHour; the tax is
// added to the fees players pay while the castle has an owner, and what it adds goes to the castle's treasury.
public static class CastleRules
{
    public const int TaxHour = 4;
    public const int SiegeMinutes = 120;
    public const int LeagueMinutes = 120;
    public const int LeagueRestartMinutes = 3;
    public const int OverSeconds = 5;

    // The coming siege of the week's siegeDay (0 Sunday .. 6 Saturday) at siegeHour, in local time: this week's, or next
    // week's once that minute has passed. A siege hour that a daylight-saving change skips does not come that week.
    public static DateTimeOffset NextSiege(DateTimeOffset local, int siegeDay, int siegeHour)
    {
        var today = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        var start = today.AddDays((siegeDay - (int)local.DayOfWeek + 7) % 7).AddHours(siegeHour);
        return start.AddMinutes(1) <= local ? start.AddDays(7) : start;
    }

    // How long a siege that starts on schedule runs: the league's restarts fill LeagueMinutes, a weekly siege SiegeMinutes.
    public static int ScheduledMinutes(CastleMode mode) => mode == CastleMode.League ? LeagueMinutes : SiegeMinutes;
    public const int FountainMaxPotions = 1440;
    public const int FountainPotionSeconds = 60;

    // The soul fountain gives one of its potions (FountainPotion) every FountainGiveSeconds to the next member of the
    // owner clan standing at it, in turn.
    public static readonly uint FountainPotion = Items.ItemCodes.MysticManaPotion;
    public const int FountainGiveSeconds = 1;

    public static bool AtFountain(System.Numerics.Vector3 position) =>
        position.X > 37040 && position.X < 37158 && position.Z > -30286 && position.Z < -30102;

    // The tax in force: the owner's rate, 0 to CastleOwnerRules.MaxTaxRate percent, and none without an owner.
    public static int TaxInForce(long ownerClanId, int tax) => ownerClanId == 0 ? 0 : Math.Clamp(tax, 0, CastleOwnerRules.MaxTaxRate);

    // A fee with the castle's tax added, whole gold rounded down, on the final price: one shop unit, an aging, a mix, an
    // alchemy's force orbs together, a teleport, a wing warp gate and a skill's training.
    public static long WithTax(long fee, int tax) => fee + fee * Math.Clamp(tax, 0, CastleOwnerRules.MaxTaxRate) / 100;

    // The tax part of a charge of units fees, each made with WithTax: a fee less its share without the tax rounded up
    // (exact, since WithTax rounds the tax down), times units.
    public static long TaxIn(long charged, int tax, int units = 1)
    {
        if (units < 1 || charged % units != 0)
        {
            return 0;
        }

        var fee = charged / units;
        var rate = 100 + Math.Clamp(tax, 0, CastleOwnerRules.MaxTaxRate);
        return (fee - (fee * 100 + rate - 1) / rate) * units;
    }
}
