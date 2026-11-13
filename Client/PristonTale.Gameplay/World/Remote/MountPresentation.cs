using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Gameplay.World.Remote;

// The travel mount (MountRules) under every rider in view: the hound stands where its rider stands, facing its way, set
// back so the rider sits over the middle of its spine, and plays its stand loop while still and its walk or run loop while
// moving, at a rate that follows the ground speed. The rider is lifted onto the hound's back (Lift) and shown standing;
// the owner of the rider's model applies both through the lift callback.
public sealed class MountPresentation : IDisposable
{
    private const string HoundInfo = "char/monster/Hound/Hound.inx";
    // The spine bone the rider sits over, and how far the back reaches above it.
    private const string SaddleBone = "Bip01 Spine2";
    private const float BackThickness = 4f;
    // Until the model is read: the saddle of the shipped hound.
    private static readonly Vector3 DefaultSaddle = new(0f, 39f, 24f);
    // Ground speeds (units per second) below which the hound stands, and above which it runs rather than walks.
    private const float StillSpeed = 5f;
    private const float WalkSpeed = MonsterLocomotion.BaseUnitsPerSecond;
    private const float RunSpeed = MonsterLocomotion.BaseUnitsPerSecond * 2f;
    private const float MinRate = 0.5f;
    private const float MaxRate = 3f;
    // A frame's speed sample moves the shown speed this share of the way, so a hitch in the pose does not flicker the gait.
    private const float SpeedSmoothing = 0.25f;

    private readonly IActorScene _scene;
    private readonly IClientLog _log;
    private readonly Task<(PristonActor Actor, Vector3 Saddle)> _hound;
    private readonly Dictionary<ulong, Rider> _riders = new();
    private readonly List<ulong> _leaving = new();

    public MountPresentation(IActorScene scene, PristonActorLoader loader, string assetRoot, IClientLog log)
    {
        _scene = scene;
        _log = log;
        _hound = Task.Run(async () =>
        {
            var actor = loader.Load(assetRoot, AssetFiles.Resolve(AssetFiles.Combine(assetRoot, HoundInfo)));
            await _scene.PrepareActorResourcesAsync(actor);
            var bone = actor.Skeleton.ObjectIndex(SaddleBone);
            var saddle = bone >= 0 ? actor.Skeleton.Objects[bone].Transform.Translation : DefaultSaddle;
            return (actor, saddle);
        });
        _ = _hound.ContinueWith(failed => _log.Warn($"[Mount] {HoundInfo} unavailable: {failed.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    // How high the rider stands above its feet: the hound's back.
    public float Lift => (_hound.IsCompletedSuccessfully ? _hound.Result.Saddle.Y : DefaultSaddle.Y) + BackThickness;

    public bool IsRiding(ulong entityId) => _riders.ContainsKey(entityId);

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
        if (!_hound.IsCompletedSuccessfully)
        {
            return;
        }

        var (actor, saddle) = _hound.Result;
        var back = Lift;
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
                lift(entityId, back);
            }

            rider.Step(at.Position, dt);
            var motion = rider.Speed < StillSpeed ? MotionCode.Stand : rider.Speed < RunSpeed ? MotionCode.Walk : MotionCode.Run;
            if (motion != rider.Motion)
            {
                rider.Motion = motion;
                _scene.SetActorMotion(rider.Handle, motion);
            }

            var rate = motion switch
            {
                MotionCode.Walk => Math.Clamp(rider.Speed / WalkSpeed, MinRate, MaxRate),
                MotionCode.Run => Math.Clamp(rider.Speed / RunSpeed, MinRate, MaxRate),
                _ => 1f
            };
            _scene.SetActorPlaybackRate(rider.Handle, rate);
            _scene.SetActorWorld(rider.Handle,
                Matrix.CreateTranslation(0f, 0f, -saddle.Z) * Matrix.CreateRotationY(at.Facing) * Matrix.CreateTranslation(at.Position));
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
