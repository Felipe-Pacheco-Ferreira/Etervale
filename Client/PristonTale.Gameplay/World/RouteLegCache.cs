using System.Collections.Concurrent;

namespace PristonTale.Gameplay.World;

// The legs route searches walked between neighbouring cells of GroundRoute's world-anchored grid, kept for every later
// search over the same loaded fields with the same body: walking a leg asks the collision hundreds of times, and a search
// again (a walk that stopped, the next objective) crosses the same cells. Only what the collision decides is kept; what
// one search alone avoids (standing NPCs) is checked on every use. Searches on several threads share it; it forgets
// everything once it holds MostLegs.
public sealed class RouteLegCache(float bodyRadius)
{
    private const int MostLegs = 200_000;
    // A leg is reused only from the height it was walked from: the same floor under the cell's centre.
    private const float SameHeight = .01f;

    private readonly ConcurrentDictionary<long, Leg> _legs = new();
    private int _added;

    // The radius of the body every leg was walked with: a mounted player's is wider than one on foot.
    public float BodyRadius { get; } = bodyRadius;

    // EndY: the feet's height at the neighbour's centre, where every leg between cells ends.
    internal readonly record struct Leg(float FromY, bool Walkable, float EndY, float Climb, bool Cramped);

    internal bool TryGet(int x, int z, int storey, int direction, float fromY, out Leg leg) =>
        _legs.TryGetValue(Key(x, z, storey, direction), out leg) && MathF.Abs(leg.FromY - fromY) <= SameHeight;

    internal void Add(int x, int z, int storey, int direction, Leg leg)
    {
        if (Interlocked.Increment(ref _added) > MostLegs)
        {
            _legs.Clear();
            Interlocked.Exchange(ref _added, 0);
        }

        _legs[Key(x, z, storey, direction)] = leg;
    }

    // 21 bits for each cell index, 18 for the storey and 4 for the direction: far more than any field spans.
    private static long Key(int x, int z, int storey, int direction) =>
        ((long)(x & 0x1F_FFFF) << 43) | ((long)(z & 0x1F_FFFF) << 22) | ((long)(storey & 0x3_FFFF) << 4) | (long)(direction & 0xF);
}
