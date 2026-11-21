using System.Numerics;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Combat;

// Why a challenge was not sent: the target is not in the world, is the challenger, is in another field, is farther than
// ArenaRadius, the field is no hunting field, or one of the two is dead, duelling or already asked by someone else.
public enum DuelChallenge
{
    Sent,
    NotFound,
    Self,
    TooFar,
    Field,
    Busy
}

// A duel: two players agree to fight, whatever their PvP mode or level, in a hunting field (PvpRules.FieldAllows). The
// target answers within InviteMs; the challenge needs it within ArenaRadius. The arena is the circle of ArenaRadius
// around the point between them when it starts. Only the two fight each other: nobody else hurts them, nor do they hurt
// anybody else. A blow never takes the last life: the blow that would leaves 1 and wins (Blow). A duellist who stays out
// of the arena OutsideMs in a row, leaves the field or the world, or dies loses; at LimitMs it is a draw. Blows take the
// PvP mode's damage percent, and nothing of the duel is lost.
public static class DuelRules
{
    public const long InviteMs = 20_000;
    public const float ArenaRadius = 500f;
    public const float OutsideMs = 5_000f;
    public const float LimitMs = 180_000f;

    public static bool FieldAllows(FieldState field) => PvpRules.FieldAllows(field);

    public static Vector3 Center(Vector3 first, Vector3 second) => (first + second) / 2f;

    // Measured on the ground plane, as every reach is.
    public static bool InArena(Vector3 center, Vector3 position) =>
        Vector2.DistanceSquared(new Vector2(center.X, center.Z), new Vector2(position.X, position.Z)) <= ArenaRadius * ArenaRadius;

    // The life a duel blow takes, and whether it wins: one that reaches the last life leaves 1.
    public static (int Damage, bool Wins) Blow(int damage, int life) => damage >= life ? (Math.Max(0, life - 1), true) : (damage, false);

    public static int SecondsLeft(float ms) => PvpRules.SecondsLeft(ms);
}
