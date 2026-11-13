using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Tests;

internal static class GroundRouteTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a goal close by behind a wall is reached around the wall, not taken as already reached", AroundWallAsync);
        yield return ("a route leg keeps the body clear of a thin post beside its line, which the wall slide's own trace misses", PostAsync);
        yield return ("a route walks around a low step it could climb, such as a hedge, and around a standing NPC", AroundHedgeAsync);
    }

    // A hedge 20 units high across x = -16..16 for z in -100..100; the goal is straight across it. Then an NPC stands on
    // the straight line over open ground.
    private static Task AroundHedgeAsync()
    {
        static float Height(Vector2 at) => MathF.Abs(at.X) <= 16f && MathF.Abs(at.Y) <= 100f ? 20f : 0f;

        static bool Leg(Vector3 feet, Vector2 target, out Vector3 end, out float climb, out bool cramped)
        {
            var from = new Vector2(feet.X, feet.Z);
            climb = 0f;
            cramped = false;
            var previous = Height(from);
            for (var sample = 1; sample <= 32; sample++)
            {
                var height = Height(Vector2.Lerp(from, target, sample / 32f));
                climb += MathF.Abs(height - previous);
                previous = height;
            }

            end = new Vector3(target.X, previous, target.Y);
            return true;
        }

        var start = new Vector3(-150f, 0f, 0f);
        var goal = new Vector2(150f, 0f);
        var route = new GroundRoute(start, goal, Leg);
        while (!route.Step(4096))
        {
        }

        var from = new Vector2(start.X, start.Z);
        var climbed = 0f;
        foreach (var point in route.Points)
        {
            Leg(new Vector3(from.X, Height(from), from.Y), point, out _, out var climb, out _);
            climbed += climb;
            from = point;
        }

        Assert.True(route.ReachesGoal && climbed == 0f, $"the route climbed {climbed} units over the hedge instead of walking around it");

        // On open ground with an NPC standing on the straight line, every leg keeps clear of it.
        var npc = new Vector2(0f, 0f);
        var around = GameScene.PlanRoute([Field(postZ: 280f)], start, goal, [npc]);
        Assert.True(around.ReachesGoal, "no route around a standing NPC");
        from = new Vector2(start.X, start.Z);
        foreach (var point in around.Points)
        {
            var leg = point - from;
            var along = Math.Clamp(Vector2.Dot(npc - from, leg) / leg.LengthSquared(), 0f, 1f);
            Assert.True(Vector2.Distance(from + leg * along, npc) >= 47f, $"a leg from {from} to {point} walks into the NPC");
            from = point;
        }

        return Task.CompletedTask;
    }

    // Plans the automatic quest's route to Skadi over Ricarten's own collision from a grid of places around her and prints
    // whether each reaches it, its legs and how long the search took, then the rest of the journey: the guide to the exit
    // towards the hunting field and, from that field's side, every hunt point in turn (--inspect-adventurer-route <assets>).
    // Each route is walked frame by frame as the player walks (Walk); a summary line per tribe ends with the stops.
    public static void InspectJourneys(string assetRoot)
    {
        Journey(assetRoot, AdventurerJourneyRoute.Tempskron, RicartenField.Definition, Forest1Field.Definition);
        Journey(assetRoot, AdventurerJourneyRoute.Morion, PillaiField.Definition, ForeverFall1Field.Definition);
    }

    private static void Journey(string assetRoot, TribeRoute tribe, FieldDef town, FieldDef hunting)
    {
        LoadedField Load(FieldDef field)
        {
            var path = field.StageSmd.Replace('\\', Path.DirectorySeparatorChar);
            var stage = new PristonSmdReader().ReadStage(AssetFiles.Resolve(Path.Combine(assetRoot, path)));
            return new LoadedField(field, stage, new PristonCollisionWorld(stage), default);
        }

        LoadedField[] fields = [Load(town), Load(hunting)];
        var collision = new FieldCollisionQuery(fields);
        var guide = new Vector2(tribe.Guide.X, -tribe.Guide.Z);
        if (!collision.TryGetFloorHeight(new Vector3(guide.X, 0, guide.Y), float.MaxValue, 0f, out var guideHeight))
        {
            throw new InvalidOperationException($"no floor under {tribe.GuideName}");
        }

        var stats = new JourneyStats();
        for (var dx = -1200; dx <= 1200; dx += 150)
        {
            for (var dz = -1200; dz <= 1200; dz += 150)
            {
                if (collision.TryGetFloorHeight(new Vector3(guide.X + dx, 0, guide.Y + dz), guideHeight + 200, 0f, out var height))
                {
                    Leg(fields, new Vector3(guide.X + dx, height, guide.Y + dz), guide, tribe.GuideName, stats);
                }
            }
        }

        var exit = tribe.ExitTowards(tribe.Guide.FieldCode, tribe.HuntField)!;
        var exitPoint = new Vector2(exit.Point.X, -exit.Point.Z);
        Leg(fields, new Vector3(guide.X, guideHeight, guide.Y), exitPoint, "the exit", stats);
        // The hunting field is entered on the ground near the exit, not on a tree or a roof above it.
        collision.TryGetFloorHeight(new Vector3(exitPoint.X, 0, exitPoint.Y), guideHeight + 400, 0f, out var ground);
        var back = tribe.ExitTowards(tribe.HuntField, tribe.Guide.FieldCode)!;
        var from = new Vector2(back.Point.X, -back.Point.Z);
        foreach (var point in tribe.HuntPoints)
        {
            var to = new Vector2(point.X, -point.Z);
            if (collision.TryGetFloorHeight(new Vector3(from.X, 0, from.Y), ground + 100, 0f, out var height))
            {
                Leg(fields, new Vector3(from.X, height, from.Y), to, "a hunt point", stats);
                ground = height;
            }

            from = to;
        }

        Console.WriteLine($"{tribe.GuideName}: {stats.Reached}/{stats.Planned} routes reach their goal, {stats.Walked} walked to the end "
            + $"without a stop, {stats.Stopped} stopped or fell; slowest search {stats.Slowest} ms");
    }

    private sealed class JourneyStats
    {
        public int Planned;
        public int Reached;
        public int Walked;
        public int Stopped;
        public long Slowest;
    }

    private static void Leg(LoadedField[] fields, Vector3 feet, Vector2 goal, string name, JourneyStats stats)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var route = GameScene.PlanRoute(fields, feet, goal, []);
        stats.Planned++;
        stats.Slowest = Math.Max(stats.Slowest, clock.ElapsedMilliseconds);
        if (clock.ElapsedMilliseconds > 1500)
        {
            Console.WriteLine($"from ({feet.X:0},{feet.Y:0},{feet.Z:0}) to {name}: SLOW {clock.ElapsedMilliseconds} ms, "
                + $"{(route.ReachesGoal ? "reaches" : "short")}, {route.Points.Count} legs");
        }
        if (!route.ReachesGoal)
        {
            var end = route.Points.Count > 0 ? route.Points[^1] : new Vector2(feet.X, feet.Z);
            Console.WriteLine($"from ({feet.X:0},{feet.Y:0},{feet.Z:0}) to {name}: SHORT, ends {Vector2.Distance(end, goal):0} away, {clock.ElapsedMilliseconds} ms");
            return;
        }

        stats.Reached++;
        var collision = new FieldCollisionQuery(fields);
        GroundFloorQuery floor = (Vector3 position, float reference, out float height) =>
            collision.TryGetFloorHeight(position, reference, GroundTraversal.StepUp, out height);
        WallTrace walls = (System.Numerics.Vector3 start, System.Numerics.Vector3 end, out System.Numerics.Vector3 hit, out System.Numerics.Vector3 normal) =>
        {
            var found = collision.TryTraceSegment(new Vector3(start.X, start.Y, start.Z), new Vector3(end.X, end.Y, end.Z), out var at, out var facing);
            hit = new System.Numerics.Vector3(at.X, at.Y, at.Z);
            normal = new System.Numerics.Vector3(facing.X, facing.Y, facing.Z);
            return found;
        };
        if (GameScene.WalkRoute(floor, walls, feet, route.Points, out var stopped))
        {
            stats.Walked++;
            return;
        }

        stats.Stopped++;
        Console.WriteLine($"from ({feet.X:0},{feet.Y:0},{feet.Z:0}) to {name}: {route.Points.Count} legs, {clock.ElapsedMilliseconds} ms, "
            + $"STOPPED at ({stopped.X:0},{stopped.Y:0})");
    }

    // A floor with a 6 x 6 post 100 units tall; the leg runs along x at z = 0, the post's centre at x = 0, z = postZ.
    private static Task PostAsync()
    {
        static bool Walks(float postZ, float side)
        {
            var collision = new FieldCollisionQuery([Field(postZ)]);
            GroundFloorQuery floor = (Vector3 position, float reference, out float height) =>
                collision.TryGetFloorHeight(position, reference, GroundTraversal.StepUp, out height);
            WallTrace walls = (System.Numerics.Vector3 start, System.Numerics.Vector3 end, out System.Numerics.Vector3 hit, out System.Numerics.Vector3 normal) =>
            {
                var found = collision.TryTraceSegment(new Vector3(start.X, start.Y, start.Z), new Vector3(end.X, end.Y, end.Z), out var at, out var facing);
                hit = new System.Numerics.Vector3(at.X, at.Y, at.Z);
                normal = new System.Numerics.Vector3(facing.X, facing.Y, facing.Z);
                return found;
            };
            return GameScene.WalksTo(new Vector3(-150f, 0f, 0f), new Vector2(150f, 0f), floor, walls, 1.25f, side, out _, out _);
        }

        Assert.True(Walks(12f, 0f), "the fixture's post stood on the line the slide traces");
        Assert.True(!Walks(12f, .75f), "a leg passed a post twelve units from its line");
        Assert.True(Walks(60f, .75f), "a post sixty units away blocked the leg");
        return Task.CompletedTask;
    }

    private static LoadedField Field(float postZ)
    {
        const float Half = 3f;
        const float Top = 100f;
        Vector3[] positions =
        [
            new(-300, 0, -300), new(-300, 0, 300), new(300, 0, 300), new(300, 0, -300),
            new(-Half, 0, postZ - Half), new(Half, 0, postZ - Half), new(Half, 0, postZ + Half), new(-Half, 0, postZ + Half),
            new(-Half, Top, postZ - Half), new(Half, Top, postZ - Half), new(Half, Top, postZ + Half), new(-Half, Top, postZ + Half)
        ];
        PristonStageFace[] faces =
        [
            new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1),
            new(4, 5, 9, 0, -1), new(4, 9, 8, 0, -1), new(5, 6, 10, 0, -1), new(5, 10, 9, 0, -1),
            new(6, 7, 11, 0, -1), new(6, 11, 10, 0, -1), new(7, 4, 8, 0, -1), new(7, 8, 11, 0, -1)
        ];
        var vertices = positions.Select(position => new PristonStageVertex(position, Color.White)).ToArray();
        var material = new PristonMaterial(true, 1, 0, 1, false, Vector3.One, 0, 1,
            PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var stage = new PristonStage("route-post-fixture.smd", [material], vertices, faces, [], [], default);
        return new LoadedField(RicartenField.Definition, stage, new PristonCollisionWorld(stage), default);
    }

    // A wall along x = 0 from z = -200 to z = 20; the goal is 40 units from the start, on its other side.
    private static Task AroundWallAsync()
    {
        static bool Crosses(Vector2 from, Vector2 to)
        {
            if (MathF.Sign(from.X) == MathF.Sign(to.X) || from.X == to.X)
            {
                return false;
            }

            var z = from.Y + (to.Y - from.Y) * (0 - from.X) / (to.X - from.X);
            return z is >= -200f and <= 20f;
        }

        static bool Leg(Vector3 feet, Vector2 target, out Vector3 end, out float climb, out bool cramped)
        {
            end = new Vector3(target.X, feet.Y, target.Y);
            climb = 0f;
            cramped = false;
            return !Crosses(new Vector2(feet.X, feet.Z), target);
        }

        var start = new Vector3(-20f, 0f, 0f);
        var goal = new Vector2(20f, 0f);
        var route = new GroundRoute(start, goal, Leg);
        while (!route.Step(4096))
        {
        }

        Assert.True(route.ReachesGoal && route.Points.Count > 0, "a goal behind a wall counted as reached from where the player stands");
        var from = new Vector2(start.X, start.Z);
        foreach (var point in route.Points)
        {
            Assert.True(!Crosses(from, point), $"the route crosses the wall between {from} and {point}");
            from = point;
        }

        Assert.True(Vector2.Distance(from, goal) <= 40f, $"the route ends at {from}, away from the goal");
        return Task.CompletedTask;
    }
}
