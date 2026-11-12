using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.World;

// Whether the body can walk straight from feet to a point of the render plane, and the feet there: the caller answers
// with its own movement, so a route holds only legs the player can actually walk.
public delegate bool RouteLeg(Vector3 feet, Vector2 target, out Vector3 end);

// A walkable route over the loaded fields: an A* over a grid of cells whose every leg is a walk the caller's movement
// accepts (RouteLeg), so the route never meets a wall the player would. The search can be spread over frames or run off
// the game thread (Step); the route is then shortened to the fewest legs that stay walkable. When the goal cannot be
// reached (it lies outside the loaded fields), the route ends at the reachable point nearest to it, and the caller
// searches again from there. Cells are told apart by height too, so stairs, bridges and the ground under them are
// separate places. A leg that is walkable but has little room on its sides (roomy fails) costs more, so the route keeps
// to the open middle of roads, squares and stairs and goes along a wall only where there is no other way.
public sealed class GroundRoute
{
    private const float Cell = 32f;
    private const float GoalReach = 40f;
    private const int MostNodes = 120_000;
    // Heights this far apart are different floors of one cell (a stair's flight over the ground, a bridge over a road).
    private const float Storey = 24f;
    private static readonly (int X, int Z)[] Neighbours = [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    private readonly RouteLeg _leg;
    private readonly RouteLeg _roomy;
    // How much more a cramped leg costs than an open one.
    private const float CrampedCost = 2.5f;
    private readonly Vector3 _start;
    private readonly Vector2 _goal;
    private readonly PriorityQueue<Node, float> _open = new();
    private readonly Dictionary<(int, int, int), float> _costs = new();
    private Node _closest;
    private int _examined;

    private sealed record Node(Vector3 Feet, int X, int Z, float Cost, Node Parent);

    // start: the feet; goal: a point of the render plane.
    public GroundRoute(Vector3 start, Vector2 goal, RouteLeg leg, RouteLeg roomy = null)
    {
        _start = start;
        _goal = goal;
        _leg = leg;
        _roomy = roomy;
        var origin = new Node(start, 0, 0, 0f, null);
        _closest = origin;
        _costs[Key(0, 0, start)] = 0f;
        _open.Enqueue(origin, Distance(start, goal));
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

        if (Distance(_start, _goal) <= GoalReach)
        {
            return Finish(new Node(_start, 0, 0, 0f, null), reachesGoal: true);
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
            }

            if (remaining <= Cell * 1.5f && TryLeg(node.Feet, _goal, out var end))
            {
                return Finish(new Node(end, node.X, node.Z, node.Cost + remaining, node), reachesGoal: true);
            }

            foreach (var (dx, dz) in Neighbours)
            {
                var x = node.X + dx;
                var z = node.Z + dz;
                var target = new Vector2(_start.X + x * Cell, _start.Z + z * Cell);
                if (!TryLeg(node.Feet, target, out var feet))
                {
                    continue;
                }

                var length = Vector2.Distance(Planar(node.Feet), target);
                var cost = node.Cost + (_roomy is null || _roomy(node.Feet, target, out _) ? length : length * CrampedCost);
                var key = Key(x, z, feet);
                if (_costs.TryGetValue(key, out var known) && known <= cost)
                {
                    continue;
                }

                _costs[key] = cost;
                _open.Enqueue(new Node(feet, x, z, cost, node), cost + Distance(feet, _goal));
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
        var chain = new List<Vector3>();
        for (var node = end; node is not null; node = node.Parent)
        {
            chain.Add(node.Feet);
        }

        chain.Reverse();
        Points = Shorten(chain);
        ReachesGoal = reachesGoal;
        Done = true;
        return true;
    }

    // Keeps a point only where the leg from the last kept one to the next would not stay clear.
    private IReadOnlyList<Vector2> Shorten(List<Vector3> chain)
    {
        var points = new List<Vector2>();
        var from = 0;
        while (from < chain.Count - 1)
        {
            var to = chain.Count - 1;
            while (to > from + 1 && !TryLeg(chain[from], Planar(chain[to]), out _))
            {
                to--;
            }

            points.Add(Planar(chain[to]));
            from = to;
        }

        return points;
    }

    private bool TryLeg(Vector3 feet, Vector2 target, out Vector3 end) => _leg(feet, target, out end);

    private static (int, int, int) Key(int x, int z, Vector3 feet) => (x, z, (int)MathF.Round(feet.Y / Storey));

    private static Vector2 Planar(Vector3 point) => new(point.X, point.Z);

    private static float Distance(Vector3 point, Vector2 goal) => Vector2.Distance(Planar(point), goal);
}
