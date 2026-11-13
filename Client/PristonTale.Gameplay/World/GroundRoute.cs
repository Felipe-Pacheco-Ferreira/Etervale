using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.World;

// Whether the body can walk straight from feet to a point of the render plane, the feet there, how far the feet rose and
// fell on the way and whether the leg only passes with little room on its sides: the caller answers with its own
// movement, so a route holds only legs the player can actually walk.
public delegate bool RouteLeg(Vector3 feet, Vector2 target, out Vector3 end, out float climb, out bool cramped);

// A walkable route over the loaded fields: an A* over a grid of cells whose every leg is a walk the caller's movement
// accepts (RouteLeg), so the route never meets a wall the player would. The search can be spread over frames or run off
// the game thread (Step); the route is then shortened to the fewest legs that stay walkable. When the goal cannot be
// reached (it lies outside the loaded fields), the route ends at the reachable point nearest to it, and the caller
// searches again from there. Cells are told apart by height too, so stairs, bridges and the ground under them are
// separate places. A cramped leg costs more, so the route keeps to open ground and passes a narrow stair or gate only
// where there is no other way; a leg that rises and falls costs more, so it walks around what it could step onto.
public sealed class GroundRoute
{
    private const float Cell = 32f;
    private const float GoalReach = 40f;
    private const int MostNodes = 120_000;
    // Heights this far apart are different floors of one cell (a stair's flight over the ground, a bridge over a road).
    private const float Storey = 24f;
    private static readonly (int X, int Z)[] Neighbours = [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    // How much more a cramped leg costs than an open one.
    private const float CrampedCost = 2.5f;
    // What each unit the feet rise or fall adds to a leg's cost: the collision lets the body step onto hedges, planters
    // and kerbs, which a player walks around; stairs, the only way up, are still taken.
    private const float ClimbCost = 4f;
    // How much more the distance left weighs than the way walked: the search heads for the goal and examines far fewer
    // cells on a long route, at the price of a route slightly longer than the shortest.
    private const float GoalWeight = 1.5f;
    // A search for a goal off the loaded ground ends once this many cells pass without coming nearer to it: its nearest
    // reachable point was found long before the search would run out of cells.
    private const int StallCells = 6000;
    // Rise and fall a shortcut may add over the legs it replaces (uneven paving, the rounding of the floor samples).
    private const float ClimbTolerance = 4f;

    private readonly RouteLeg _leg;
    private readonly Vector3 _start;
    private readonly Vector2 _goal;
    private readonly bool _goalOffGround;
    private readonly PriorityQueue<Node, float> _open = new();
    private readonly Dictionary<(int, int, int), float> _costs = new();
    private Node _closest;
    private int _examined;
    private int _closestAt;

    // Cramped: whether the leg into this cell passes with little room.
    private sealed record Node(Vector3 Feet, int X, int Z, float Cost, bool Cramped, Node Parent);

    // start: the feet; goal: a point of the render plane. goalOffGround: the loaded fields have no floor at the goal (it
    // lies in a field not loaded yet), so the search stops once it stops coming nearer (StallCells).
    public GroundRoute(Vector3 start, Vector2 goal, RouteLeg leg, bool goalOffGround = false)
    {
        _start = start;
        _goal = goal;
        _leg = leg;
        _goalOffGround = goalOffGround;
        var origin = new Node(start, 0, 0, 0f, false, null);
        _closest = origin;
        _costs[Key(0, 0, start)] = 0f;
        _open.Enqueue(origin, Distance(start, goal) * GoalWeight);
    }

    public bool Done { get; private set; }

    // Whether the route ends at the goal rather than at the nearest reachable point.
    public bool ReachesGoal { get; private set; }

    // The points to walk through after the start, on the render plane.
    public IReadOnlyList<Vector2> Points { get; private set; } = [];

    // Examines up to budget cells; true once the route is known.
    public bool Step(int budget)
    {
        if (Done)
        {
            return true;
        }

        // Near is not reached when a wall or a ledge stands between (the goal below a terrace's parapet).
        if (Distance(_start, _goal) <= GoalReach && _leg(_start, _goal, out _, out _, out _))
        {
            return Finish(new Node(_start, 0, 0, 0f, false, null), reachesGoal: true);
        }

        while (budget-- > 0 && _examined < MostNodes && _open.TryDequeue(out var node, out _))
        {
            if (_costs[Key(node.X, node.Z, node.Feet)] < node.Cost)
            {
                continue;
            }

            _examined++;
            var remaining = Distance(node.Feet, _goal);
            if (remaining < Distance(_closest.Feet, _goal))
            {
                _closest = node;
                _closestAt = _examined;
            }
            else if (_goalOffGround && _examined - _closestAt > StallCells)
            {
                return Finish(_closest, reachesGoal: false);
            }

            if (remaining <= Cell * 1.5f && _leg(node.Feet, _goal, out var end, out _, out var tight))
            {
                return Finish(new Node(end, node.X, node.Z, node.Cost + remaining, tight, node), reachesGoal: true);
            }

            foreach (var (dx, dz) in Neighbours)
            {
                var x = node.X + dx;
                var z = node.Z + dz;
                var target = new Vector2(_start.X + x * Cell, _start.Z + z * Cell);
                if (!_leg(node.Feet, target, out var feet, out var climb, out var cramped))
                {
                    continue;
                }

                var length = Vector2.Distance(Planar(node.Feet), target);
                var cost = node.Cost + (cramped ? length * CrampedCost : length) + climb * ClimbCost;
                var key = Key(x, z, feet);
                if (_costs.TryGetValue(key, out var known) && known <= cost)
                {
                    continue;
                }

                _costs[key] = cost;
                _open.Enqueue(new Node(feet, x, z, cost, cramped, node), cost + Distance(feet, _goal) * GoalWeight);
            }
        }

        if (_open.Count == 0 || _examined >= MostNodes)
        {
            return Finish(_closest, reachesGoal: false);
        }

        return false;
    }

    private bool Finish(Node end, bool reachesGoal)
    {
        var chain = new List<Node>();
        for (var node = end; node is not null; node = node.Parent)
        {
            chain.Add(node);
        }

        chain.Reverse();
        Points = Shorten(chain);
        ReachesGoal = reachesGoal;
        Done = true;
        return true;
    }

    // Keeps a point only where the leg from the last kept one to the next would not stay clear.
    private List<Vector2> Shorten(List<Node> chain)
    {
        var points = new List<Vector2>();
        var from = 0;
        while (from < chain.Count - 1)
        {
            var to = chain.Count - 1;
            while (to > from + 1 && !Keeps(chain, from, to))
            {
                to--;
            }

            points.Add(Planar(chain[to].Feet));
            from = to;
        }

        return points;
    }

    // A shortcut replaces the legs between two points only when it is walkable, climbs no more than they did and is
    // cramped only where one of them was: shortening never puts back the hedge or the wall the search kept away from.
    private bool Keeps(List<Node> chain, int from, int to)
    {
        if (!_leg(chain[from].Feet, Planar(chain[to].Feet), out _, out var climb, out var cramped))
        {
            return false;
        }

        var climbed = 0f;
        var wasCramped = false;
        for (var index = from; index < to; index++)
        {
            climbed += MathF.Abs(chain[index + 1].Feet.Y - chain[index].Feet.Y);
            wasCramped |= chain[index + 1].Cramped;
        }

        return climb <= climbed + ClimbTolerance && (!cramped || wasCramped);
    }

    private static (int, int, int) Key(int x, int z, Vector3 feet) => (x, z, (int)MathF.Round(feet.Y / Storey));

    private static Vector2 Planar(Vector3 point) => new(point.X, point.Z);

    private static float Distance(Vector3 point, Vector2 goal) => Vector2.Distance(Planar(point), goal);
}
