using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Motion;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World;

// The local player's travel mount (MountRules). TryMount predicts the mount at once and asks the server, whose answer is
// the Mounted flag of the player's own description; a refused or lost request falls back to that flag after
// MountAnswerMs. Attacking, casting, being hurt, dying, a forced warp and an action field leave the mount here as they do
// on the server. Every rider in view, this player included, is drawn on its hound by MountPresentation.
public sealed partial class GameScene
{
    private const double MountAnswerMs = 2000d;

    private MountPresentation _mounts;
    private Func<ulong, (Vector3 Position, float Facing)?> _mountPose;
    private Action<ulong, float> _mountLift;
    private bool _mounted;
    private bool _serverMounted;
    private bool _mountAsked;
    // What the request waiting for an answer asked: to mount or to leave the mount.
    private bool _mountWanted;
    private double _mountAskedMs;
    private float _mountCombatMsLeft;
    // How high the local player's model is drawn: its hound's back while it rides one.
    private float _riderLift;

    // Riding the travel mount: the local player moves at MountRules.UnitsPerSecond.
    public bool Mounted => _mounted;

    // False when the player may not mount now: dead, swinging or casting, in combat, in an action field or offline.
    public bool TryMount()
    {
        if (_mounted)
        {
            return true;
        }

        if (_playChannel is null || _playerBody is null || _activeField is null
            || !MountRules.MayMount(!PlayerDead, !PlayerActing && !_playerController.LocomotionFrozen, Overweight, _mountCombatMsLeft,
                _activeField.Definition.State))
        {
            return false;
        }

        AskMount(true);
        return true;
    }

    public void Dismount()
    {
        if (_mounted || _mountAsked && _mountWanted)
        {
            AskMount(false);
        }
    }

    private void AskMount(bool mount)
    {
        SetMounted(mount);
        if (_mountAsked && _mountWanted == mount)
        {
            // The same request is still waiting for its answer: neither sent again nor given more time.
            return;
        }

        _mountWanted = mount;
        _mountAsked = true;
        _mountAskedMs = _skillClockMs;
        SendUpstream(_playChannel.SendMountAsync(mount, _presenceCts.Token), "mount");
    }

    private void SetMounted(bool mounted)
    {
        _mounted = mounted;
        if (_remote is not null)
        {
            _mounts?.SetRider(_remote.SelfEntityId, mounted);
        }
    }

    // A swing, a cast or a hit of the local player: it leaves the mount and may not mount again for a while.
    private void LeaveMountForCombat()
    {
        _mountCombatMsLeft = MountRules.CombatHoldMs;
        Dismount();
    }

    private void CreateMount()
    {
        if (_mounts is null && _renderer is not null)
        {
            _mounts = new MountPresentation(_actorScene, _actorLoader, _assetRoot ?? "", _log);
        }
    }

    private void ObserveMounts(PresenceUpdate update) => _mounts?.Observe(update);

    // The server's word on the local player's mount; a request it has not answered yet keeps the predicted state.
    private void ObserveOwnMount(EntitySpawnInfo spawn)
    {
        if (spawn.Player is not { } appearance)
        {
            return;
        }

        _serverMounted = appearance.Mounted;
        if (!_mountAsked || _serverMounted == _mounted)
        {
            _mountAsked = false;
            SetMounted(_serverMounted);
        }
    }

    private void UpdateMount(float dt)
    {
        _mountCombatMsLeft = MathF.Max(0f, _mountCombatMsLeft - dt * 1000f);
        if (_mountAsked && _skillClockMs - _mountAskedMs > MountAnswerMs)
        {
            _mountAsked = false;
            SetMounted(_serverMounted);
        }

        if (_mounted && (PlayerDead || _activeField is not null && !MountRules.FieldAllows(_activeField.Definition.State)))
        {
            Dismount();
        }

        _mounts?.Update(dt, _mountPose ??= MountPose, _mountLift ??= LiftRider);
    }

    private (Vector3 Position, float Facing)? MountPose(ulong entityId) =>
        IsSelf(entityId) ? (GroundTraversal.ToVisualPosition(_playerBody.Position), PlayerYaw) : PetOwnerPose(entityId);

    private void LiftRider(ulong entityId, float lift)
    {
        if (!IsSelf(entityId))
        {
            _remote?.SetRiderLift(entityId, lift);
            return;
        }

        // The lift follows the hound's gait every few frames; the shown motion changes only on getting on or off.
        var seated = _riderLift > 0f;
        _riderLift = lift;
        ApplyPlayerWorld();
        if (seated != lift > 0f && _playerController.Current is MotionCode.Walk or MotionCode.Run)
        {
            SetPlayerMotion(_playerController.Current);
        }
    }

    // A rider sits still on its hound while the hound walks or runs under it.
    private MotionCode ShownPlayerMotion(MotionCode motion) =>
        _riderLift > 0f && motion is MotionCode.Walk or MotionCode.Run ? MotionCode.Stand : motion;
}
