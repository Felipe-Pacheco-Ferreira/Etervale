using System.Numerics;

namespace PristonTale.Simulation.World;

// Finds a bounded, walkable route inside one loaded field. Each edge is checked against the same floor and wall queries
// used by movement prediction. Callers run the search outside their update and render loops.
public static class FieldRoute
{
    private const int CellSize = 48;
    private const float ProbeSpacing = 8f;
    private const int NodeLimit = 4096;
    private const float GoalRadius = CellSize;
    private static readonly (int X, int Z)[] Neighbours =
        [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    private readonly record struct Cell(int X, int Z, int Height);
    private sealed record Node(Vector3 Position, float Cost, Node Parent);

    public static IReadOnlyList<Vector3> Find(FieldTerrain terrain, Vector3 origin, Vector3 goal, CancellationToken cancellationToken = default)
    {
        if (!terrain.HasGeometry || !Finite(origin) || !Finite(goal))
        {
            return [];
        }

        var start = new Node(origin, 0f, null);
        var open = new PriorityQueue<Node, float>();
        var visited = new Dictionary<Cell, float> { [Key(origin, origin)] = 0f };
        open.Enqueue(start, PlanarDistance(origin, goal));
        var examined = 0;
        while (open.TryDequeue(out var node, out _) && examined++ < NodeLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited[Key(node.Position, origin)] < node.Cost)
            {
                continue;
            }

            var remaining = PlanarDistance(node.Position, goal);
            if (remaining <= GoalRadius && Trace(terrain, node.Position, goal, out var end)
                && MathF.Abs(end.Y - goal.Y) <= FieldTerrain.StepUp)
            {
                var route = new List<Vector3> { end };
                for (var step = node; step.Parent is not null; step = step.Parent)
                {
                    route.Add(step.Position);
                }
                route.Reverse();
                return route;
            }

            foreach (var (x, z) in Neighbours)
            {
                var target = node.Position + new Vector3(x * CellSize, 0f, z * CellSize);
                if (!Trace(terrain, node.Position, target, out var position))
                {
                    continue;
                }

                var cost = node.Cost + PlanarDistance(node.Position, position);
                var cell = Key(position, origin);
                if (visited.TryGetValue(cell, out var old) && old <= cost)
                {
                    continue;
                }

                visited[cell] = cost;
                open.Enqueue(new Node(position, cost, node), cost + PlanarDistance(position, goal));
            }
        }

        return [];
    }

    private static bool Trace(FieldTerrain terrain, Vector3 from, Vector3 target, out Vector3 reached)
    {
        var count = Math.Max(1, (int)MathF.Ceiling(PlanarDistance(from, target) / ProbeSpacing));
        reached = from;
        for (var index = 1; index <= count; index++)
        {
            var probe = Vector3.Lerp(from, target, index / (float)count) with { Y = reached.Y };
            var slid = terrain.Slide(reached, probe);
            if (PlanarDistance(slid, probe) > .25f || !terrain.TryFloorNear(probe, FieldTerrain.StepUp, out var floor))
            {
                return false;
            }
            reached = probe with { Y = floor };
        }

        return true;
    }

    private static Cell Key(Vector3 position, Vector3 origin) => new(
        (int)MathF.Round((position.X - origin.X) / CellSize),
        (int)MathF.Round((position.Z - origin.Z) / CellSize),
        (int)MathF.Round(position.Y / FieldTerrain.StepUp));

    private static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
