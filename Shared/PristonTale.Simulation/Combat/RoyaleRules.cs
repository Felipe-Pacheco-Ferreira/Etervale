using System.Numerics;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Combat;

// Why a Battle Royale registration was refused.
public enum RoyaleJoin : byte
{
    Registered,
    Left,
    Closed,
    Level,
    Full,
    Busy,
    NotRegistered
}

// The Battle Royale, a last-survivor event in the quest arena's field. Registration opens RegistrationMinutes before each
// of the server's hours (royale.hours) for players of MinimumLevel or more; at the hour, MinimumPlayers to MaximumPlayers
// of them (the first registered) fight everyone else, each as a stand-in of KitLevel with its class's kit (RoyaleKits),
// its own equipment and character untouched. Fewer registered cancel it. The safe zone (Radius) shrinks every
// ZoneStepMs for ZoneSteps steps; outside it a player loses ZoneDamagePercent of its life a second. Nobody dies: a blow or
// the zone that would kill leaves 1 life and takes the player out to watch. The last one standing wins; at LimitMs the
// standing players rank by the damage they dealt. Every PotionEveryMs potions appear in the zone, one per
// PlayersPerPotion standing, drunk when picked up for PotionPercent of the whole life or mana. The first PrizePlaces win gold; everyone who fought gains
// ExperiencePercent of the experience its level still lacks.
public static class RoyaleRules
{
    public const int MinimumLevel = 80;
    public const int MinimumPlayers = 10;
    public const int MaximumPlayers = 50;
    public const int RegistrationMinutes = 10;
    public const int KitLevel = 100;
    public const float ZoneStepMs = 120_000f;
    public const int ZoneSteps = 5;
    public const int ZoneDamagePercent = 5;
    public const float ZoneDamageEveryMs = 1_000f;
    public const int LimitMinutes = 15;
    public const float LimitMs = LimitMinutes * 60_000f;
    public const float PotionEveryMs = 30_000f;
    public const int PlayersPerPotion = 5;
    public const int PotionPercent = 30;
    public const int ExperiencePercent = 2;
    public const int PrizePlaces = 3;
    public const int FieldCode = QuestArena.FieldCode;
    public const string DefaultHours = "16,21";

    // The zone's centre, the middle of the arena's walled floor, its first radius and its last.
    public static readonly Vector3 Center = new(22145f, 0f, 9739f);
    public const float FirstRadius = 1000f;
    public const float LastRadius = 150f;

    // The players start spread on this circle around the centre.
    public const float StartRadius = 700f;

    // The zone's radius after the steps taken so far (0 to ZoneSteps), shrinking evenly.
    public static float Radius(int steps) => FirstRadius - (FirstRadius - LastRadius) * Math.Clamp(steps, 0, ZoneSteps) / ZoneSteps;

    public static int StepsAfter(float elapsedMs) => Math.Min(ZoneSteps, (int)(elapsedMs / ZoneStepMs));

    public static bool InZone(Vector3 position, float radius)
    {
        var x = position.X - Center.X;
        var z = position.Z - Center.Z;
        return x * x + z * z <= radius * radius;
    }

    // Registration is open in the RegistrationMinutes before one of the hours (EventSchedule.HourMask).
    public static bool RegistrationOpen(int hourMask, DateTimeOffset local) =>
        MinutesToStart(local) <= RegistrationMinutes && (hourMask & 1 << (local.Hour + 1) % HoursPerDay) != 0;

    // The minutes until the next hour begins.
    public static int MinutesToStart(DateTimeOffset local) => MinutesPerHour - local.Minute;

    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;

    // The event starts as one of its hours begins.
    public static bool Starts(int hourMask, DateTimeOffset local) => (hourMask & 1 << local.Hour) != 0 && local.Minute == 0;

    // Where the index-th of count players starts: evenly around the start circle.
    public static Vector3 Place(int index, int count)
    {
        var angle = MathF.Tau * index / Math.Max(1, count);
        return Center + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * StartRadius;
    }

    // A point inside the zone from two rolls in [0, 1): uniform over its disc, kept off its edge (PotionReach of the
    // radius), where the next shrink would leave it.
    public static Vector3 PointInZone(float radius, float angleRoll, float distanceRoll)
    {
        var angle = MathF.Tau * angleRoll;
        var distance = radius * MathF.Sqrt(distanceRoll) * PotionReach;
        return Center + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance;
    }

    private const float PotionReach = 0.9f;

    // The potions the event lays, in turn, and what a picked one gives back of the whole life or mana.
    public static readonly uint LifePotion = Items.ItemCodes.Parse("PL104");
    public static readonly uint ManaPotion = Items.ItemCodes.Parse("PM104");

    public static bool IsPotion(uint code) => code == LifePotion || code == ManaPotion;

    public static int PotionRestore(int whole) => whole * PotionPercent / 100;

    // The life the zone takes a second, never below 1.
    public static int ZoneDamage(int maxLife) => Math.Max(1, maxLife * ZoneDamagePercent / 100);

    public static int PotionsFor(int standing) => standing <= 0 ? 0 : Math.Max(1, standing / PlayersPerPotion);

    // The event's end: one or nobody standing, or the time limit.
    public static bool Over(int standing, float elapsedMs) => standing <= 1 || elapsedMs >= LimitMs;

    // The experience the event gives: a share of what the level still lacks, nothing at the last level.
    public static ulong Experience(int level, ulong experience)
    {
        if (level >= ExperienceTable.MaximumLevel)
        {
            return 0;
        }

        var next = ExperienceTable.ForLevel(level + 1);
        return next <= experience ? 0 : (next - experience) * ExperiencePercent / 100;
    }

    // The event's key as stored (yyyymmddhh, server-local), one per event.
    public static long EventKey(DateTimeOffset local) => local.Year * 1_000_000L + local.Month * 10_000L + local.Day * 100L + local.Hour;
}
