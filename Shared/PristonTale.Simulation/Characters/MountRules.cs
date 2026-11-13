using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Characters;

// The travel mount: a hound the player rides to reach a quest's places faster. Ridden, the player moves at SpeedFactor
// times its run speed whatever its walk or run setting, unless overweight, when it walks as on foot. It mounts only
// alive, idle, not overweight, out of combat and outside action fields, and leaves the mount when it attacks, casts, is
// hurt, dies or is warped.
public static class MountRules
{
    public const float SpeedFactor = 1.6f;

    // A swing, a cast or a hit keeps the player in combat this long; it may not mount meanwhile.
    public const float CombatHoldMs = 5000f;

    // The planar radius of the mounted body, wider than the player on foot: the hound reaches about 40 units ahead of and
    // behind the rider, so a route for a mounted player keeps this far from walls.
    public const float BodyRadius = 40f;

    public static float UnitsPerSecond(int moveSpeed) => Locomotion.RunUnitsPerSecond(moveSpeed) * SpeedFactor;

    public static bool FieldAllows(FieldState state) => state != FieldState.Action;

    // idle: no swing, cast, flinch, meal, cheer or open shop of its own.
    public static bool MayMount(bool alive, bool idle, bool overweight, float combatMsLeft, FieldState field) =>
        alive && idle && !overweight && combatMsLeft <= 0f && FieldAllows(field);

    // Whether a rider moves at the mounted speed: an overweight one walks as on foot.
    public static bool RidesFast(bool mounted, bool overweight) => mounted && !overweight;
}
