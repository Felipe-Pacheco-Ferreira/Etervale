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

    // Walks to a point of the render plane as a tap on the ground there; false when the player cannot move now.
    public bool WalkTo(Vector2 destination)
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
            new NumVector2(_playerBody.PlanarPosition.X, _playerBody.PlanarPosition.Y), new NumVector2(destination.X, destination.Y), PlayerRadius);
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
    // read-only, so the search reads it alongside the frame). Null when the player is not placed yet.
    public Task<GroundRoute> PlanRouteAsync(Vector2 goal)
    {
        if (_playerBody is null)
        {
            return null;
        }

        var collision = new FieldCollisionQuery(_fields.ToArray());
        GroundFloorQuery floor = (Vector3 position, float reference, out float height) =>
            collision.TryGetFloorHeight(position, reference, GroundTraversal.StepUp, out height);
        WallTrace walls = (NumVector3 start, NumVector3 end, out NumVector3 hit, out NumVector3 normal) =>
        {
            var found = collision.TryTraceSegment(new Vector3(start.X, start.Y, start.Z), new Vector3(end.X, end.Y, end.Z), out var at, out var facing);
            hit = new NumVector3(at.X, at.Y, at.Z);
            normal = new NumVector3(facing.X, facing.Y, facing.Z);
            return found;
        };
        var route = new GroundRoute(PlayerFeet, goal,
            (Vector3 feet, Vector2 target, out Vector3 end) => WalksTo(feet, target, floor, walls, RouteClearance, out end),
            (Vector3 feet, Vector2 target, out Vector3 end) => WalksTo(feet, target, floor, walls, RouteRoom, out end));
        return Task.Run(() =>
        {
            while (!route.Step(RouteSearchBatch))
            {
            }

            return route;
        });
    }

    // How much wider than the body a route keeps clear of walls, the length of each simulated step and the largest drop a
    // route walks off.
    private const float RouteClearance = 1.25f;
    // The room a leg needs on each side, in body radii, to count as open ground the route prefers.
    private const float RouteRoom = 3f;
    private const float RouteStep = 12f;
    private const float RouteMostDrop = FieldTerrain.DropTolerance;
    private const int RouteSearchBatch = 2048;

    // Walks the body from feet straight to target the way UpdatePlayerInput moves it (the wall slide at body height, then
    // the floor), a step at a time, with the body widened by RouteClearance: true only when no step is turned aside or
    // stopped, no climb is more than a step and no drop is a ledge. width: the body's radius in this many radii.
    private static bool WalksTo(Vector3 feet, Vector2 target, GroundFloorQuery floor, WallTrace walls, float width, out Vector3 end)
    {
        end = feet;
        var start = new Vector2(feet.X, feet.Z);
        var total = Vector2.Distance(start, target);
        if (total < 1e-3f)
        {
            return true;
        }

        var direction = (target - start) / total;
        var clearance = PlayerRadius * width;
        var current = feet;
        for (var travelled = 0f; travelled < total;)
        {
            var length = MathF.Min(RouteStep, total - travelled);
            var wanted = new Vector2(current.X, current.Z) + direction * length;
            var probe = new NumVector3(current.X, current.Y - GroundTraversal.VisualOffset + BodyProbeHeight, current.Z);
            var slid = WallSlide.Slide(probe, new NumVector2(direction.X * length, direction.Y * length), clearance, clearance, walls);
            if (Vector2.DistanceSquared(new Vector2(slid.X, slid.Z), wanted) > .25f
                || !GroundTraversal.TryFollow(current, wanted, floor, out var reached, out var drop) || drop > RouteMostDrop)
            {
                return false;
            }

            current = reached;
            travelled += length;
        }

        end = current;
        return true;
    }

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
