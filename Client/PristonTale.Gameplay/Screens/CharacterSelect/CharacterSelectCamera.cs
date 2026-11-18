using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

// The screen's camera: on the rail of CharacterSelectStage, dollying between the steps' distances and swaying.
internal sealed class CharacterSelectCamera
{
    private const float WebLineupTargetX = 10f;
    private const float LoadingCameraSeconds = 0.8f;
    private static readonly Vector3 LoadingCameraEye = new(0f, 360f, 810f);
    private static readonly Vector3 LoadingCameraTarget = new(0f, 38f, 35f);
    private readonly PristonCamera _camera;
    private readonly bool _usesWebBackdrop;
    private float _distance;
    private readonly float _distanceScale;
    private int _currentState;
    private float _swaySeconds;
    private float _loadingCamera;

    public CharacterSelectCamera(GraphicsDevice graphicsDevice, int initialState, float distanceScale = 1f, PristonCamera sharedCamera = null)
    {
        _distanceScale = distanceScale;
        _usesWebBackdrop = sharedCamera is not null;
        _currentState = initialState;
        _distance = CharacterSelectStage.RailDistance(initialState);
        _camera = sharedCamera ?? new PristonCamera(new Vector3(0f, CharacterSelectStage.CameraTargetY, 0f), Vector3.Forward);
        if (sharedCamera is null)
        {
            Apply(graphicsDevice, 0f);
        }
        else
        {
            _swaySeconds = CharacterSelectStage.SwaySeconds * 0.25f;
        }
    }

    public PristonCamera Camera => _camera;
    internal bool UsesWebBackdrop => _usesWebBackdrop;
    public Matrix View => _camera.View;
    public Matrix Projection => _camera.Projection;
    public Vector3 Position => _camera.Position;
    public bool HasReached(int step) => _currentState == step && _distance == CharacterSelectStage.RailDistance(step);

    public bool AtClassFraming =>
        _distance >= CharacterSelectStage.RailDistance(CharacterSelectStage.TribeStep)
        && _distance <= CharacterSelectStage.RailDistance(CharacterSelectStage.ClassStep);

    public void Update(GraphicsDevice graphicsDevice, int targetState, float dt, bool loadingWorld = false)
    {
        (_distance, _currentState) = _usesWebBackdrop
            ? DollyWeb(_distance, _currentState, targetState, dt)
            : CharacterSelectStage.Dolly(_distance, _currentState, targetState, dt);
        _swaySeconds += dt;
        var loadingStep = (loadingWorld && _usesWebBackdrop ? dt : -dt) / LoadingCameraSeconds;
        _loadingCamera = Math.Clamp(_loadingCamera + loadingStep, 0f, 1f);
        Apply(graphicsDevice, CharacterSelectStage.Sway(_swaySeconds));
    }

    internal static (float Distance, int Step) DollyWeb(float distance, int currentState, int targetState, float dt)
    {
        var destination = CharacterSelectStage.RailDistance(targetState);
        var from = CharacterSelectStage.RailDistance(currentState);
        var difference = destination - distance;
        var speed = MathF.Abs(difference) < MathF.Abs(destination - from) / 16f
            ? CharacterSelectStage.DollySlowPerSecond : CharacterSelectStage.DollyFastPerSecond;
        var step = speed * dt;
        return MathF.Abs(difference) <= step + CharacterSelectStage.DollySnap
            ? (destination, targetState)
            : (distance + MathF.CopySign(step, difference), currentState);
    }

    private void Apply(GraphicsDevice graphicsDevice, float sway)
    {
        var (eye, target) = Pose(_distance * _distanceScale, sway, _usesWebBackdrop);
        var fieldOfView = CharacterSelectStage.CameraFieldOfView;
        if (_loadingCamera > 0f)
        {
            var blend = MathHelper.SmoothStep(0f, 1f, _loadingCamera);
            eye = Vector3.Lerp(eye, LoadingCameraEye, blend);
            target = Vector3.Lerp(target, LoadingCameraTarget, blend);
            fieldOfView = MathHelper.Lerp(fieldOfView, MathHelper.ToRadians(49f), blend);
        }

        _camera.SetScriptedView(graphicsDevice, eye, target, Vector3.Up, fieldOfView);
    }

    internal static (Vector3 Eye, Vector3 Target) Pose(float distance, float sway, bool webBackdrop = false)
    {
        var target = new Vector3(0f, CharacterSelectStage.CameraTargetY + sway, 0f);
        if (webBackdrop)
        {
            (distance, target) = FrameWebStage(distance, target);
        }

        var radians = CharacterSelectStage.CameraAngle * GameUnits.RadiansPerAngle;
        var offset = new Vector3(0f, distance * MathF.Sin(radians), distance * MathF.Cos(radians));
        return (target + offset, target);
    }

    private static (float Distance, Vector3 Target) FrameWebStage(float distance, Vector3 target)
    {
        var roster = CharacterSelectStage.RailDistance(CharacterSelectStage.RosterStep);
        var tribe = CharacterSelectStage.RailDistance(CharacterSelectStage.TribeStep);
        var lineup = CharacterSelectStage.RailDistance(CharacterSelectStage.ClassStep);
        var appearance = CharacterSelectStage.RailDistance(CharacterSelectStage.NameStep);
        if (distance <= lineup)
        {
            // The tribe cards and the lineup use the same framing. Only the roster moves in from its closer view.
            var framing = (distance - roster) / (tribe - roster);
            target.X += MathHelper.SmoothStep(0f, WebLineupTargetX, framing);
            target.Y += MathHelper.SmoothStep(5f, -30f, framing);
            return (MathHelper.SmoothStep(250f, 490f, framing), target);
        }

        var portraitFraming = (distance - lineup) / (appearance - lineup);
        target.X = MathHelper.SmoothStep(WebLineupTargetX, -10f, portraitFraming);
        target.Y -= MathHelper.SmoothStep(30f, 44f, portraitFraming);
        return (MathHelper.SmoothStep(490f, 505f, portraitFraming), target);
    }
}
