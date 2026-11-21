using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Combat;

// The answer to a player's PvP switch: turned on, turned off, or refused (level below MinimumLevel, a blow between it
// and another player in the last CombatHoldMs, still a murderer).
public enum PvpSwitch
{
    On,
    Off,
    LevelTooLow,
    InCombat,
    Murderer
}

// Players fighting each other outside Bless Castle (the PvP mode; the castle keeps its siege rules, CastleSiegeRules).
// A player from MinimumLevel turns its PvP on at once; turning it off waits until CombatHoldMs passed without a blow
// between it and another player, and never while it is a murderer. Two players fight in a hunting field (FieldAllows)
// when both have PvP on and they share neither party nor clan; a murderer is fought by any player with PvP on, its own
// party and clan too. Killing a player who is not a murderer makes the killer a murderer for MurdererMs. Players hurt
// each other for DamagePercent of the damage (the server's pvp.damage_percent), and a player slain by another loses
// nothing (DeathPenalty).
public static class PvpRules
{
    public const int MinimumLevel = 50;
    public const float CombatHoldMs = 60_000f;
    public const int MurdererMinutes = 10;
    public const float MurdererMs = MurdererMinutes * 60_000f;
    public const int DefaultDamagePercent = 50;
    public const int MaxDamagePercent = 1000;

    public static bool FieldAllows(FieldState field) =>
        field is FieldState.Forest or FieldState.Ruin or FieldState.Desert or FieldState.Dungeon or FieldState.Iron or FieldState.Ice;

    // What asking for PvP on (wanted) or off does to a player whose PvP is not already that way.
    public static PvpSwitch Switch(bool wanted, int level, float combatMsLeft, float murdererMsLeft)
    {
        if (wanted)
        {
            return level >= MinimumLevel ? PvpSwitch.On : PvpSwitch.LevelTooLow;
        }

        if (murdererMsLeft > 0f)
        {
            return PvpSwitch.Murderer;
        }

        return combatMsLeft > 0f ? PvpSwitch.InCombat : PvpSwitch.Off;
    }

    public static bool MayFight(FieldState field, bool attackerOn, bool targetOn, bool targetMurderer, bool sameParty, bool sameClan) =>
        FieldAllows(field) && attackerOn && targetOn && (targetMurderer || !sameParty && !sameClan);

    public static bool SameClan(long attackerClan, long targetClan) => attackerClan != 0 && attackerClan == targetClan;

    // A blow's damage between players at this percent; a blow that hurts still takes at least 1.
    public static int Damage(int damage, int percent) => damage <= 0 ? damage : (int)Math.Clamp((long)damage * percent / 100, 1L, int.MaxValue);

    public static bool MakesMurderer(bool victimMurderer) => !victimMurderer;

    // The whole seconds of a hold or a mark still running, for the player's notice.
    public static int SecondsLeft(float ms) => (int)MathF.Ceiling(Math.Max(0f, ms) / 1000f);
}
