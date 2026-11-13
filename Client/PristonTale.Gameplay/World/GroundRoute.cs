using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.World;

// Whether the body can walk straight from feet to a point of the render plane, the feet there, how far the feet rose and
// fell on the way and whether the leg only passes with little room on its sides: the caller answers with its own
// movement, so a route holds only legs the player can actually walk.
public delegate bool RouteLeg(Vector3 feet, Vector2 target, out Vector3 end, out float climb, out bool cramped);

// Whether a walkable leg keeps clear of what one search alone avoids (standing NPCs), which a RouteLegCache cannot hold.
public delegate bool RouteClear(Vector3 feet, Vector2 end);

// How long GameScene.PlanRoute took: its searches and simulated walks, how many searches ran, how many cells they
// examined and how many legs between cells were walked or reused from the RouteLegCache.
public readonly record struct RouteTiming(double SearchMilliseconds, double WalkMilliseconds, int Searches, int Cells, int LegsWalked, int LegsReused);

// A walkable route over the loaded fields: an A* over a grid of cells whose every leg is a walk the caller's movement
// accepts (RouteLeg), so the route never meets a wall the player would. The search can be spread over frames or run off
// the game thread (Step); the route is then shortened to the fewest legs that stay walkable. When the goal cannot be
// reached (it lies outside the loaded fields), the route ends at the reachable point nearest to it, and the caller
// searches again from there. Cells are told apart by height too, so stairs, bridges and the ground under them are
// separate places. A cramped leg costs more, so the route keeps to open ground and passes a narrow stair or gate only
// where there is no other way; a leg that rises and falls costs more, so it walks around what it could step onto.
// The grid is anchored to the world (cell centres at multiples of Cell), so every search meets the same legs between
// cells: with a RouteLegCache they are walked once for all searches over the same fields, those a node needs walked
// in parallel. Only the start, off the grid, has legs of its own.
public sealed class GroundRoute
{
    private const float Cell = 32f;
    private const float GoalReach = 40f;
    // The cells a whole search may examine, and those a stretch (a search that only leads the way part of a long walk)
    // may: a stretch ends at the reachable point nearest the goal, fast enough to start walking at once.
    public const int WholeSearch = 120_000;
    public const int Stretch = 2_500;
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
    // How many points of the route a shortcut may pass over in one round of shortening; rounds repeat over the points
    // kept, so a long straight route still ends as a few legs, without trying a leg between every two of its points.
    private const int ShortcutReach = 24;
    // The longest straight walk tried before the search: a failed one stops at the first wall, so its cost stays small.
    private const float DirectReach = 2000f;

    private readonly RouteLeg _startLeg;
    private readonly RouteLeg _leg;
    private readonly RouteClear _clear;
    private readonly RouteLegCache _cache;
    private readonly Vector3 _start;
    private readonly Vector2 _goal;
    private readonly bool _goalOffGround;
    private readonly PriorityQueue<Node, float> _open = new();
    private readonly Dictionary<(int, int, int), float> _costs = new();
    private readonly Walked[] _walked = new Walked[Neighbours.Length];
    private readonly int[] _pending = new int[Neighbours.Length];
    private Node _closest;
    private int _closestAt;
    private bool _triedDirect;
    private readonly int _mostCells;

    // Cramped: whether the leg into this cell passes with little room. OnGrid: false for the start, off the grid.
    private sealed record Node(Vector3 Feet, int X, int Z, float Cost, bool Cramped, Node Parent, bool OnGrid);

    private readonly record struct Walked(bool Walkable, float EndY, float Climb, bool Cramped);

    // start: the feet; goal: a point of the render plane; leg: every leg. goalOffGround: the loaded fields have no floor at
    // the goal (it lies in a field not loaded yet), so the search stops once it stops coming nearer (StallCells).
    public GroundRoute(Vector3 start, Vector2 goal, RouteLeg leg, bool goalOffGround = false)
        : this(start, goal, leg, leg, null, null, goalOffGround)
    {
    }

    // startLeg: the legs out of the start, whole (what this search avoids included). leg: every other leg, as the
    // collision alone decides it, then checked by clear; legs between cells are kept in cache, so leg must give the same
    // answer for the same cells on any search and be safe to call from several threads at once. mostCells: WholeSearch,
    // or Stretch for a search that only leads part of the way.
    public GroundRoute(Vector3 start, Vector2 goal, RouteLeg startLeg, RouteLeg leg, RouteClear clear, RouteLegCache cache, bool goalOffGround,
        int mostCells = WholeSearch)
    {
        _mostCells = mostCells;
        _start = start;
        _goal = goal;
        _startLeg = startLeg;
        _leg = leg;
        _clear = clear;
        _cache = cache;
        _goalOffGround = goalOffGround;
        var origin = new Node(start, (int)MathF.Round(start.X / Cell), (int)MathF.Round(start.Z / Cell), 0f, false, null, OnGrid: false);
        _closest = origin;
        _costs[KeyOf(origin)] = 0f;
        _open.Enqueue(origin, Distance(start, goal) * GoalWeight);
    }

    public bool Done { get; private set; }

    // Whether the route ends at the goal rather than at the nearest reachable point.
    public bool ReachesGoal { get; private set; }

    // The points to walk through after the start, on the render plane.
    public IReadOnlyList<Vector2> Points { get; private set; } = [];

    // The feet where the route ends: the goal, or the reachable point nearest to it, where the next search starts.
    public Vector3 End { get; private set; }

    public int Examined { get; private set; }

    public int LegsWalked { get; private set; }

    public int LegsReused { get; private set; }

    public RouteTiming Timing { get; internal set; }

    // Examines up to budget cells; true once the route is known.
    public bool Step(int budget)
    {
        if (Done)
        {
            return true;
        }

        if (!_triedDirect)
        {
            _triedDirect = true;
            // Near is not reached when a wall or a ledge stands between (the goal below a terrace's parapet).
            var distance = Distance(_start, _goal);
            if (distance <= GoalReach && _startLeg(_start, _goal, out _, out _, out _))
            {
                return Finish(new Node(_start, 0, 0, 0f, false, null, OnGrid: false), reachesGoal: true);
            }

            // A goal in plain sight is walked to straight, without a search: the leg is the one the shortening would keep
            // (open, no more rise and fall than uneven paving), so the route is the same, found at once.
            if (distance <= DirectReach && _startLeg(_start, _goal, out var end, out var climb, out var cramped) && !cramped && climb <= ClimbTolerance)
            {
                var origin = new Node(_start, 0, 0, 0f, false, null, OnGrid: false);
                return Finish(new Node(end, 0, 0, distance, false, origin, OnGrid: false), reachesGoal: true);
            }
        }

        while (budget-- > 0 && Examined < _mostCells && _open.TryDequeue(out var node, out _))
        {
            if (_costs[KeyOf(node)] < node.Cost)
            {
                continue;
            }

            Examined++;
            var remaining = Distance(node.Feet, _goal);
            if (remaining < Distance(_closest.Feet, _goal))
            {
                _closest = node;
                _closestAt = Examined;
            }
            else if (_goalOffGround && Examined - _closestAt > StallCells)
            {
                return Finish(_closest, reachesGoal: false);
            }

            if (remaining <= Cell * 1.5f && Walks(node, _goal, out var end, out _, out var tight))
            {
                return Finish(new Node(end, node.X, node.Z, node.Cost + remaining, tight, node, node.OnGrid), reachesGoal: true);
            }

            if (node.OnGrid)
            {
                ExpandCell(node);
            }
            else
            {
                ExpandStart(node);
            }
        }

        if (_open.Count == 0 || Examined >= _mostCells)
        {
            return Finish(_closest, reachesGoal: false);
        }

        return false;
    }

    // The start's legs go to the cells around it, every one within a cell and a half on both axes.
    private void ExpandStart(Node node)
    {
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var x = node.X + dx;
                var z = node.Z + dz;
                var target = new Vector2(x * Cell, z * Cell);
                if (_startLeg(node.Feet, target, out var end, out var climb, out var cramped))
                {
                    Open(node, x, z, end.Y, climb, cramped);
                }
            }
        }
    }

    private void ExpandCell(Node node)
    {
        var storey = StoreyOf(node.Feet.Y);
        var pending = 0;
        for (var direction = 0; direction < Neighbours.Length; direction++)
        {
            if (_cache is not null && _cache.TryGet(node.X, node.Z, storey, direction, node.Feet.Y, out var known))
            {
                _walked[direction] = new Walked(known.Walkable, known.EndY, known.Climb, known.Cramped);
                LegsReused++;
            }
            else
            {
                _pending[pending++] = direction;
            }
        }

        LegsWalked += pending;
        if (_cache is not null && pending > 1)
        {
            Parallel.For(0, pending, index => WalkNeighbour(node, storey, _pending[index]));
        }
        else
        {
            for (var index = 0; index < pending; index++)
            {
                WalkNeighbour(node, storey, _pending[index]);
            }
        }

        for (var direction = 0; direction < Neighbours.Length; direction++)
        {
            var walked = _walked[direction];
            var x = node.X + Neighbours[direction].X;
            var z = node.Z + Neighbours[direction].Z;
            if (walked.Walkable && (_clear is null || _clear(node.Feet, new Vector2(x * Cell, z * Cell))))
            {
                Open(node, x, z, walked.EndY, walked.Climb, walked.Cramped);
            }
        }
    }

    // Each call writes only its own direction's slot, so the node's legs can be walked at once.
    private void WalkNeighbour(Node node, int storey, int direction)
    {
        var target = new Vector2((node.X + Neighbours[direction].X) * Cell, (node.Z + Neighbours[direction].Z) * Cell);
        var walkable = _leg(node.Feet, target, out var end, out var climb, out var cramped);
        _walked[direction] = new Walked(walkable, end.Y, climb, cramped);
        _cache?.Add(node.X, node.Z, storey, direction, new RouteLegCache.Leg(node.Feet.Y, walkable, end.Y, climb, cramped));
    }

    // The feet stand on the cell's centre: a leg's end is its target, up to the rounding of its steps.
    private void Open(Node from, int x, int z, float height, float climb, bool cramped)
    {
        var feet = new Vector3(x * Cell, height, z * Cell);
        var length = Vector2.Distance(Planar(from.Feet), Planar(feet));
        var cost = from.Cost + (cramped ? length * CrampedCost : length) + climb * ClimbCost;
        var key = (x, z, StoreyOf(height));
        if (_costs.TryGetValue(key, out var known) && known <= cost)
        {
            return;
        }

        _costs[key] = cost;
        _open.Enqueue(new Node(feet, x, z, cost, cramped, from, OnGrid: true), cost + Distance(feet, _goal) * GoalWeight);
    }

    private bool Walks(Node from, Vector2 target, out Vector3 end, out float climb, out bool cramped) =>
        from.OnGrid ? Walks(from.Feet, target, out end, out climb, out cramped) : _startLeg(from.Feet, target, out end, out climb, out cramped);

    private bool Walks(Vector3 from, Vector2 target, out Vector3 end, out float climb, out bool cramped) =>
        _leg(from, target, out end, out climb, out cramped) && (_clear is null || _clear(from, Planar(end)));

    private bool Finish(Node end, bool reachesGoal)
    {
        var chain = new List<Node>();
        for (var node = end; node is not null; node = node.Parent)
        {
            chain.Add(node);
        }

        chain.Reverse();
        Points = Shorten(chain);
        End = end.Feet;
        ReachesGoal = reachesGoal;
        Done = true;
        return true;
    }

    // Keeps a point only where the leg from the last kept one to a later one would not stay clear. Each round looks at
    // most ShortcutReach kept points ahead and rounds repeat until one keeps every point; no leg is tried twice.
    private List<Vector2> Shorten(List<Node> chain)
    {
        var kept = new List<int>(chain.Count);
        for (var index = 0; index < chain.Count; index++)
        {
            kept.Add(index);
        }

        var refused = new HashSet<(int, int)>();
        while (true)
        {
            var next = new List<int>(kept.Count) { kept[0] };
            var from = 0;
            while (from < kept.Count - 1)
            {
                var to = Math.Min(from + ShortcutReach, kept.Count - 1);
                while (to > from + 1 && !Keeps(chain, kept[from], kept[to], refused))
                {
                    to--;
                }

                next.Add(kept[to]);
                from = to;
            }

            var settled = next.Count == kept.Count;
            kept = next;
            if (settled)
            {
                break;
            }
        }

        var points = new List<Vector2>(kept.Count - 1);
        for (var index = 1; index < kept.Count; index++)
        {
            points.Add(Planar(chain[kept[index]].Feet));
        }

        return points;
    }

    // A shortcut replaces the legs between two points only when it is walkable, climbs no more than they did and is
    // cramped only where one of them was: shortening never puts back the hedge or the wall the search kept away from.
    private bool Keeps(List<Node> chain, int from, int to, HashSet<(int, int)> refused)
    {
        if (refused.Contains((from, to)))
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

        if (Walks(chain[from], Planar(chain[to].Feet), out _, out var climb, out var cramped) && climb <= climbed + ClimbTolerance
            && (!cramped || wasCramped))
        {
            return true;
        }

        refused.Add((from, to));
        return false;
    }

    private static (int, int, int) KeyOf(Node node) => node.OnGrid ? (node.X, node.Z, StoreyOf(node.Feet.Y)) : (int.MinValue, int.MinValue, 0);

    private static int StoreyOf(float height) => (int)MathF.Round(height / Storey);

    private static Vector2 Planar(Vector3 point) => new(point.X, point.Z);

    private static float Distance(Vector3 point, Vector2 goal) => Vector2.Distance(Planar(point), goal);
}
