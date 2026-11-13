using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Gameplay.World.Remote;

// The travel mount (MountRules) under every rider in view: a Hopy, drawn at MountScale, stands where its rider stands,
// facing its way, set ahead so the rider clings to its back, and plays its stand loop while still and its hopping walk
// while moving (it has no run), at a rate that follows the ground speed. The rider sits astride with its arms around the
// mount (ActorSeat puts its pelvis at its origin) and rides the gait: each frame it is lifted to the saddle bone's height
// plus the back's skin above it (taken once from the first mount shown, IActorScene.TryGetActorSaddle); the owner of the
// rider's model applies both through the lift callback.
public sealed class MountPresentation : IDisposable
{
    private const string MountInfo = "char/monster/hopy/hopy.inx";
    // The monster stands a little lower than a player; at this scale the rider's arms reach round its shoulders.
    private const float MountScale = 1.6f;
    // The rider clings to the back: SeatBehind units behind the saddle bone, at the height of the back's skin there under
    // its crest and horn (SkinShare of the skin's points lie below it), with the rider's pelvis SeatDepth above it.
    private const string SaddleBone = "Bip02";
    private const float SeatBehind = 6.5f;
    private const float SkinShare = .4f;
    private const float SeatDepth = 3f;
    // Until the first mount's skin is read: the shipped Hopy's.
    private static readonly Vector3 DefaultSaddle = new(0f, 7f, -6.5f);
    // A lift that moved less than this is not passed on.
    private const float LiftStep = .25f;
    // Ground speed (units per second) below which the mount stands, and the speed its walk loop is authored for.
    private const float StillSpeed = 5f;
    private const float WalkSpeed = MonsterLocomotion.BaseUnitsPerSecond;
    private const float MinRate = 0.5f;
    private const float MaxRate = 3f;
    // A frame's speed sample moves the shown speed this share of the way, so a hitch in the pose does not flicker the gait.
    private const float SpeedSmoothing = 0.25f;

    private readonly IActorScene _scene;
    private readonly IClientLog _log;
    private readonly Task<PristonActor> _mount;
    private readonly Dictionary<ulong, Rider> _riders = new();
    private readonly List<ulong> _leaving = new();
    private Vector3 _saddle = DefaultSaddle;
    private bool _saddleFound;

    public MountPresentation(IActorScene scene, PristonActorLoader loader, string assetRoot, IClientLog log)
    {
        _scene = scene;
        _log = log;
        _mount = Task.Run(async () =>
        {
            var actor = loader.Load(assetRoot, AssetFiles.Resolve(AssetFiles.Combine(assetRoot, MountInfo)));
            await _scene.PrepareActorResourcesAsync(actor);
            return actor;
        });
        _ = _mount.ContinueWith(failed => _log.Warn($"[Mount] {MountInfo} unavailable: {failed.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    // Other players' mounts from their descriptions; the local player's is set by its scene (SetRider), which predicts it.
    public void Observe(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            if (!spawn.IsSelf && spawn.Player is { } player)
            {
                SetRider(spawn.EntityId, player.Mounted);
            }
        }

        foreach (var removed in update.Removed)
        {
            SetRider(removed, false);
        }
    }

    public void SetRider(ulong entityId, bool mounted)
    {
        if (mounted)
        {
            _riders.TryAdd(entityId, new Rider());
        }
        else if (_riders.Remove(entityId, out var rider))
        {
            rider.Remove(_scene);
            _leaving.Add(entityId);
        }
    }

    // pose: the rider's drawn feet and facing (yaw), or null when it is not drawn. lift: tells the owner of a rider's model
    // how high to draw it (0 once it leaves the mount); called only when that changes.
    public void Update(float dt, Func<ulong, (Vector3 Position, float Facing)?> pose, Action<ulong, float> lift)
    {
        foreach (var entityId in _leaving)
        {
            lift(entityId, 0f);
        }

        _leaving.Clear();
        if (!_mount.IsCompletedSuccessfully)
        {
            return;
        }

        var actor = _mount.Result;
        foreach (var (entityId, rider) in _riders)
        {
            if (pose(entityId) is not { } at)
            {
                continue;
            }

            if (!rider.HasActor)
            {
                rider.Handle = _scene.AddActor(new PristonActorInstance(actor, Matrix.Identity, MotionCode.Stand));
                rider.HasActor = true;
                rider.Motion = MotionCode.Stand;
                rider.Last = at.Position;
            }

            if (!_saddleFound && _scene.TryGetActorSaddle(rider.Handle, SaddleBone, SeatBehind, SkinShare, out var saddle))
            {
                _saddle = saddle;
                _saddleFound = true;
            }

            rider.Step(at.Position, dt);
            var motion = rider.Speed < StillSpeed ? MotionCode.Stand : MotionCode.Walk;
            if (motion != rider.Motion)
            {
                rider.Motion = motion;
                _scene.SetActorMotion(rider.Handle, motion);
            }

            // The walk loop plays faster with the ground speed (a mount scaled up takes longer strides).
            var rate = motion == MotionCode.Walk ? Math.Clamp(rider.Speed / (WalkSpeed * MountScale), MinRate, MaxRate) : 1f;
            _scene.SetActorPlaybackRate(rider.Handle, rate);
            _scene.SetActorWorld(rider.Handle, Matrix.CreateTranslation(-_saddle.X, 0f, -_saddle.Z) * Matrix.CreateScale(MountScale)
                * Matrix.CreateRotationY(at.Facing) * Matrix.CreateTranslation(at.Position));

            if (!_scene.TryGetActorBoneWorld(rider.Handle, SaddleBone, out var spine))
            {
                continue;
            }

            var back = spine.Translation.Y - at.Position.Y + _saddle.Y * MountScale + SeatDepth;
            if (MathF.Abs(rider.Lift - back) >= LiftStep)
            {
                rider.Lift = back;
                lift(entityId, back);
            }
        }
    }

    public void Dispose()
    {
        foreach (var rider in _riders.Values)
        {
            rider.Remove(_scene);
        }

        _riders.Clear();
        _leaving.Clear();
    }

    private sealed class Rider
    {
        public ActorHandle Handle { get; set; }
        public bool HasActor { get; set; }
        // The height the rider's owner was last told (lift callback).
        public float Lift { get; set; }
        public MotionCode Motion { get; set; }
        public Vector3 Last { get; set; }
        public float Speed { get; private set; }

        public void Step(Vector3 position, float dt)
        {
            if (dt > 0f)
            {
                var planar = new Vector2(position.X - Last.X, position.Z - Last.Z).Length() / dt;
                Speed += (planar - Speed) * SpeedSmoothing;
            }

            Last = position;
        }

        public void Remove(IActorScene scene)
        {
            if (HasActor)
            {
                scene.RemoveActor(Handle);
                HasActor = false;
            }
        }
    }
}
