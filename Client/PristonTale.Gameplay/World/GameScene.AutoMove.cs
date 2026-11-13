using Microsoft.Xna.Framework;
using NumVector2 = System.Numerics.Vector2;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;
using NumVector3 = System.Numerics.Vector3;

namespace PristonTale.Gameplay.World;

// Movement a client drives without a pointer (the Android automatic quest): walking to a point, approaching an NPC and
// picking up a drop, each as the matching tap would.
public sealed partial class GameScene
{
    // Whether the player is walking, approaching someone or going for a drop.
    public bool AutoMoving => _hasMoveTarget || _npcTargetId != 0 || _lootInteraction is { Busy: true };

    // Whether a warp is moving the player, during which nothing it is asked to do starts.
    public bool Warping => _pendingWarp is not null || _pendingFieldWarp is not null;

    // Walks to a point of the render plane as a tap on the ground there; false when the player cannot move now. A tap
    // within the body's radius does not move it; a route's point is walked to within arriveWithin instead, so the walk
    // stays on the legs the route checked.
    public bool WalkTo(Vector2 destination, float arriveWithin = PlayerRadius)
    {
        if (!CanAutoMove())
        {
            return false;
        }

        var changed = !_hasMoveTarget || Vector2.DistanceSquared(_moveTarget, destination) > PlayerRadius * PlayerRadius;
        if (_attackInput.TargetId != 0 || _npcTargetId != 0)
        {
            ClearAttackTarget();
        }

        _lootInteraction?.Cancel();
        _moveTarget = destination;
        _hasMoveTarget = !DestinationMovement.WithinDistance(
            new NumVector2(_playerBody.PlanarPosition.X, _playerBody.PlanarPosition.Y), new NumVector2(destination.X, destination.Y), arriveWithin);
        if (changed)
        {
            _reports.Request();
        }

        return true;
    }

    // Walks up to an NPC and talks to it once in reach, as a tap on it.
    public bool ApproachNpc(ulong entityId)
    {
        if (!CanAutoMove() || _remote is null || !_remote.TryGetTarget(entityId, out _, out _))
        {
            return false;
        }

        if (_npcTargetId == entityId)
        {
            return true;
        }

        _lootInteraction?.Cancel();
        ClearAttackTarget();
        _npcTargetId = entityId;
        _approach = ApproachKind.Npc;
        _npcMoveSent = false;
        _targetSelection.Trace(entityId);
        StopPredictedMovement();
        return true;
    }

    // Goes for the drop nearest to the player within radius, as a tap on it; false when there is none.
    public bool PickUpNear(float radius)
    {
        if (!CanAutoMove() || _groundLoot is null || _lootInteraction is null || _lootInteraction.Busy
            || !_groundLoot.FindNearest(_playerBody.Position, radius, out var id, out var position))
        {
            return false;
        }

        ClearAttackTarget();
        _npcTargetId = 0;
        _lootInteraction.Select(id);
        _moveTarget = new Vector2(position.X, position.Z);
        _hasMoveTarget = true;
        _reports.Request();
        return true;
    }

    // A route from the player to a point of the render plane, over the same floor and walls the player collides with and
    // a little wider than the body, searched off the game thread over the fields loaded now (their collision is
    // read-only, so the search reads it alongside the frame). avoid: where standing NPCs are, on the render plane; the
    // body collides with them, which the field's collision does not know. Null when the player is not placed yet.
    public Task<GroundRoute> PlanRouteAsync(Vector2 goal, IReadOnlyList<Vector2> avoid = null)
    {
        if (_playerBody is null)
        {
            return null;
        }

        var fields = _fields.ToArray();
        var feet = PlayerFeet;
        return Task.Run(() => PlanRoute(fields, feet, goal, avoid ?? []));
    }

    // A route the player's own movement walks to its end: each route found is walked frame by frame (WalkRoute); where
    // the walk stops or falls, that place is avoided like an NPC and the search runs again, up to RouteWalkAttempts.
    internal static GroundRoute PlanRoute(IReadOnlyList<LoadedField> fields, Vector3 feet, Vector2 goal, IReadOnlyList<Vector2> avoid)
    {
        var collision = new FieldCollisionQuery(fields);
        var floor = FloorOf(collision);
        var walls = WallsOf(collision);
        var blocked = new List<Vector2>(avoid);
        GroundRoute route = null;
        for (var attempt = 0; attempt < RouteWalkAttempts; attempt++)
        {
            route = Search(CreateRoute(collision, floor, walls, feet, goal, blocked));
            if (route.Points.Count == 0 || WalkRoute(floor, walls, feet, route.Points, out var stopped))
            {
                return route;
            }

            blocked.Add(stopped);
        }

        return route;
    }

    private const int RouteWalkAttempts = 4;

    // Walks a route frame by frame as UpdatePlayerInput moves the player towards a tapped point at a run, 60 frames a
    // second (DestinationMovement, the wall slide at body height with the body's radius, the floor), each point to within
    // a unit as QuestNavigator follows it. False, and where, when the walk stops for half a second or drops past
    // GroundFall.SnapDistance between two frames (a fall).
    internal static bool WalkRoute(GroundFloorQuery floor, WallTrace walls, Vector3 feet, IReadOnlyList<Vector2> points, out Vector2 stopped)
    {
        const float Frame = 1f / 60f;
        var speed = Locomotion.UnitsPerSecond(1, true, true);
        var position = new Vector2(feet.X, feet.Z);
        var height = feet.Y;
        var index = 0;
        var still = 0;
        stopped = position;
        for (var frame = 0; frame < RouteWalkFrames && index < points.Count; frame++)
        {
            if (Vector2.Distance(position, points[index]) <= RoutePointReach)
            {
                index++;
                continue;
            }

            var step = DestinationMovement.Advance(new NumVector2(position.X, position.Y), new NumVector2(points[index].X, points[index].Y), speed, Frame);
            var wanted = new NumVector2(step.Position.X - position.X, step.Position.Y - position.Y);
            var probe = new NumVector3(position.X, height - GroundTraversal.VisualOffset + BodyProbeHeight, position.Y);
            var slid = WallSlide.Slide(probe, wanted, PlayerRadius, PlayerRadius, walls);
            GroundTraversal.TryFollow(new Vector3(position.X, height, position.Y), new Vector2(slid.X, slid.Z), floor, out var reached, out var drop);
            var moved = Vector2.Distance(position, new Vector2(reached.X, reached.Z));
            position = new Vector2(reached.X, reached.Z);
            height = reached.Y;
            still = moved < wanted.Length() * .5f ? still + 1 : 0;
            if (drop > GroundFall.SnapDistance || still > RouteWalkStill)
            {
                stopped = position;
                return false;
            }
        }

        return index >= points.Count;
    }

    // How close the player comes to each point of a route (QuestNavigator walks to within it), how many frames a walk
    // may stand still before it counts as stopped, and the longest walk simulated.
    public const float RoutePointReach = 1f;
    private const int RouteWalkStill = 30;
    private const int RouteWalkFrames = 60 * 120;

    private static GroundFloorQuery FloorOf(FieldCollisionQuery collision) =>
        (Vector3 position, float reference, out float height) => collision.TryGetFloorHeight(position, reference, GroundTraversal.StepUp, out height);

    private static WallTrace WallsOf(FieldCollisionQuery collision) =>
        (NumVector3 start, NumVector3 end, out NumVector3 hit, out NumVector3 normal) =>
        {
            var found = collision.TryTraceSegment(new Vector3(start.X, start.Y, start.Z), new Vector3(end.X, end.Y, end.Z), out var at, out var facing);
            hit = new NumVector3(at.X, at.Y, at.Z);
            normal = new NumVector3(facing.X, facing.Y, facing.Z);
            return found;
        };

    private static GroundRoute Search(GroundRoute route)
    {
        while (!route.Step(RouteSearchBatch))
        {
        }

        return route;
    }

    // The route search over these fields' collision, its legs walked as the player walks, keeping NpcRoom from the points
    // to avoid. A leg keeps RouteBody of side room, or else, cramped and costlier, RouteNarrowBody: the route passes a stair
    // or a gate narrower than RouteBody only where there is no other way.
    private static GroundRoute CreateRoute(FieldCollisionQuery collision, GroundFloorQuery floor, WallTrace walls, Vector3 feet, Vector2 goal,
        IReadOnlyList<Vector2> avoid)
    {
        // A short leg out of where the player stands needs no side room and may pass NPCs, or a player against a wall or
        // beside an NPC could never leave; a longer one (a shortened route's first leg) keeps both.
        return new GroundRoute(feet, goal,
            (Vector3 from, Vector2 target, out Vector3 end, out float climb, out bool cramped) =>
            {
                var escape = from == feet && Vector2.Distance(new Vector2(from.X, from.Z), target) <= RouteEscape;
                cramped = false;
                if (!WalksTo(from, target, floor, walls, RouteClearance, escape ? 0f : RouteBody, out end, out climb))
                {
                    cramped = true;
                    if (escape || !WalksTo(from, target, floor, walls, RouteClearance, RouteNarrowBody, out end, out climb))
                    {
                        return false;
                    }
                }

                return escape || ClearOf(new Vector2(from.X, from.Z), new Vector2(end.X, end.Z), avoid);
            },
            goalOffGround: !collision.TryGetFloorHeight(new Vector3(goal.X, 0f, goal.Y), float.MaxValue, 0f, out _));
    }

    // The room a route keeps from a standing NPC's centre: its body and the player's.
    private const float NpcRoom = PlayerRadius * 2f;
    // How long a leg out of the player's own place may be and still skip the side room and the NPCs: one diagonal cell.
    private const float RouteEscape = 48f;

    private static bool ClearOf(Vector2 from, Vector2 to, IReadOnlyList<Vector2> avoid)
    {
        var leg = to - from;
        var lengthSquared = leg.LengthSquared();
        for (var index = 0; index < avoid.Count; index++)
        {
            var along = lengthSquared < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(avoid[index] - from, leg) / lengthSquared, 0f, 1f);
            if (Vector2.DistanceSquared(from + leg * along, avoid[index]) < NpcRoom * NpcRoom)
            {
                return false;
            }
        }

        return true;
    }

    // Whether the player can walk straight to a point of the render plane from where it stands, as the straight walk of
    // ApproachNpc would: a guide close by on another storey (below a terrace's parapet) is not.
    public bool WalksStraightTo(Vector2 goal)
    {
        if (_playerBody is null)
        {
            return false;
        }

        _wallTrace ??= TraceWalls;
        return WalksTo(PlayerFeet, goal, _groundFloorQuery, _wallTrace, 1f, RouteBody, out _, out _);
    }

    // How far ahead of the body, in body radii, a route keeps clear of walls, the length of each simulated step and the
    // largest drop a route walks off.
    private const float RouteClearance = 1.25f;
    // The room every leg keeps on each side of its line, in body radii: the wall slide traces only the line itself, so
    // without it a route grazes lamp posts, planters and railings with the body.
    private const float RouteBody = .75f;
    // The side room of a cramped leg, for stairs and gates narrower than RouteBody: still more than the player strays from
    // a leg (it walks to each point to within a unit), so it never brushes their sides.
    private const float RouteNarrowBody = .3f;
    // Side traces on each side of the line, spread evenly out to the side room, so a thin post between two is still seen.
    private const int RouteSideTraces = 2;
    private const float RouteStep = 12f;
    // A route steps down less than the body does without falling (a larger drop plays the fall), with a margin for the
    // player's own floor samples falling at other places than the route's.
    private const float RouteMostDrop = GroundFall.SnapDistance;
    private const int RouteSearchBatch = 2048;

    // Walks the body from feet straight to target the way UpdatePlayerInput moves it (the wall slide at body height, then
    // the floor), a step at a time: true only when no step is turned aside or stopped, nothing stands within side radii of
    // its line, no climb is more than a step and no drop is a ledge. width: how far ahead the slide looks, in body radii.
    // climb: how far the feet rose and fell on the way, added up step by step.
    internal static bool WalksTo(Vector3 feet, Vector2 target, GroundFloorQuery floor, WallTrace walls, float width, float side, out Vector3 end,
        out float climb)
    {
        end = feet;
        climb = 0f;
        var start = new Vector2(feet.X, feet.Z);
        var total = Vector2.Distance(start, target);
        if (total < 1e-3f)
        {
            return true;
        }

        var direction = (target - start) / total;
        var clearance = PlayerRadius * width;
        var room = PlayerRadius * side;
        var across = new NumVector3(-direction.Y, 0f, direction.X);
        var current = feet;
        for (var travelled = 0f; travelled < total;)
        {
            var length = MathF.Min(RouteStep, total - travelled);
            var wanted = new Vector2(current.X, current.Z) + direction * length;
            var probe = new NumVector3(current.X, current.Y - GroundTraversal.VisualOffset + BodyProbeHeight, current.Z);
            var slid = WallSlide.Slide(probe, new NumVector2(direction.X * length, direction.Y * length), clearance, clearance, walls);
            if (Vector2.DistanceSquared(new Vector2(slid.X, slid.Z), wanted) > .25f || Crowded(probe, direction * length, across * room, walls)
                || !FollowsFloor(current, wanted, floor, out var reached, ref climb) || Uneven(reached, new Vector2(across.X, across.Z) * room, floor))
            {
                return false;
            }

            current = reached;
            travelled += length;
        }

        end = current;
        return true;
    }

    // Whether a wall stands beside one step: out from the line to the side room, then along the step at evenly spread
    // offsets on both sides. Floor-like faces are ground, as in the slide.
    private static bool Crowded(NumVector3 probe, Vector2 step, NumVector3 reach, WallTrace walls)
    {
        if (reach.LengthSquared() < 1e-6f)
        {
            return false;
        }

        var forward = new NumVector3(step.X, 0f, step.Y);
        if (Blocks(probe, probe + reach, walls) || Blocks(probe, probe - reach, walls))
        {
            return true;
        }

        for (var trace = 1; trace <= RouteSideTraces; trace++)
        {
            var offset = reach * ((float)trace / RouteSideTraces);
            if (Blocks(probe + offset, probe + offset + forward, walls) || Blocks(probe - offset, probe - offset + forward, walls))
            {
                return true;
            }
        }

        return false;
    }

    // Follows the floor from feet to target one unit at a time. The player samples the floor once or twice a frame, two
    // to four units apart depending on the frame rate (GroundTraversal), and stops at a rise of more than StepUp
    // between two samples; where its samples fall is chance. So the route holds every rise between any two of its
    // samples up to RouteFloorWindow apart under RouteStepUp: wherever the player's samples fall, at any frame rate, it
    // does not stop. A drop only plays the fall, so it is held under RouteMostDrop over the two units a frame covers at
    // 60 frames a second.
    private static bool FollowsFloor(Vector3 feet, Vector2 target, GroundFloorQuery floor, out Vector3 reached, ref float climb)
    {
        reached = feet;
        var start = new Vector2(feet.X, feet.Z);
        var total = Vector2.Distance(start, target);
        Span<float> recent = stackalloc float[RouteFloorWindow];
        recent.Fill(feet.Y);
        var samples = (int)MathF.Ceiling(total / RouteFloorSample);
        for (var sample = 1; sample <= samples; sample++)
        {
            var planar = Vector2.Lerp(start, target, sample / (float)samples);
            if (!floor(new Vector3(planar.X, reached.Y, planar.Y), reached.Y, out var height) || !float.IsFinite(height))
            {
                return false;
            }

            for (var back = 1; back <= RouteFloorWindow; back++)
            {
                var earlier = recent[(sample - back + RouteFloorWindow * 2) % RouteFloorWindow];
                if (height - earlier > RouteStepUp || back <= RouteDropWindow && earlier - height > RouteMostDrop)
                {
                    return false;
                }
            }

            climb += MathF.Abs(height - reached.Y);
            recent[sample % RouteFloorWindow] = height;
            reached = new Vector3(planar.X, height, planar.Y);
        }

        return true;
    }

    // The route's floor samples, and how many of them cover the farthest apart two of the player's own can be.
    private const float RouteFloorSample = 1f;
    private const int RouteFloorWindow = 4;
    private const int RouteDropWindow = 2;
    // The highest rise a route allows between two samples that close: one under the body's own StepUp.
    private const float RouteStepUp = GroundTraversal.StepUp - 1f;

    // Whether the floor beside the feet, at the side traces' offsets, rises more than a step or falls more than the body
    // steps down without falling: a stair's side, a railing's base or a kerb lower than the slide's trace height, which
    // only the floor shows, or the edge of a drop.
    private static bool Uneven(Vector3 feet, Vector2 reach, GroundFloorQuery floor)
    {
        if (reach.LengthSquared() < 1e-6f)
        {
            return false;
        }

        for (var trace = 1; trace <= RouteSideTraces; trace++)
        {
            var offset = reach * ((float)trace / RouteSideTraces);
            for (var side = -1; side <= 1; side += 2)
            {
                var point = new Vector3(feet.X + offset.X * side, feet.Y, feet.Z + offset.Y * side);
                if (!floor(point, feet.Y, out var height) || height - feet.Y > RouteStepUp || feet.Y - height > GroundFall.SnapDistance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Blocks(NumVector3 start, NumVector3 end, WallTrace walls) =>
        walls(start, end, out _, out var normal) && MathF.Abs(normal.Y) < WallSlide.FloorNormalY;

    // Where an entity in view stands on the render plane.
    public bool TryGetEntityPlanar(ulong entityId, out Vector2 position)
    {
        if (_remote is not null && _remote.TryGetTarget(entityId, out var at, out _))
        {
            position = new Vector2(at.X, at.Z);
            return true;
        }

        position = default;
        return false;
    }

    public Vector2 PlayerPlanar => _playerBody?.PlanarPosition ?? Vector2.Zero;

    // Stops a walk this side started, leaving an attack alone.
    public void StopAutoMove()
    {
        if (_hasMoveTarget || _npcTargetId != 0)
        {
            _npcTargetId = 0;
            StopPredictedMovement();
            _reports.Request();
        }
    }

    private bool CanAutoMove() =>
        _playerBody is not null && _activeField is not null && !_playerController.IsDead && !MovementLocked && !Warping && !SelfCasting;
}
