using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

// The screen's camera: on the rail of CharacterSelectStage, dollying between the steps' distances and swaying.
internal sealed class CharacterSelectCamera
{
    private readonly PristonCamera _camera;
    private readonly Vector3 _target;
    private float _distance;
    private readonly float _distanceScale;
    private int _currentState;
    private float _swaySeconds;

    public CharacterSelectCamera(GraphicsDevice graphicsDevice, int initialState, float distanceScale = 1f)
    {
        _distanceScale = distanceScale;
        _target = new Vector3(0f, CharacterSelectStage.CameraTargetY, 0f);
        _currentState = initialState;
        _distance = CharacterSelectStage.RailDistance(initialState);
        _camera = new PristonCamera(_target, Vector3.Forward);
        Apply(graphicsDevice, 0f);
    }

    public PristonCamera Camera => _camera;
    public Matrix View => _camera.View;
    public Matrix Projection => _camera.Projection;
    public Vector3 Position => _camera.Position;

    public void Update(GraphicsDevice graphicsDevice, int targetState, float dt)
    {
        (_distance, _currentState) = CharacterSelectStage.Dolly(_distance, _currentState, targetState, dt);
        _swaySeconds += dt;
        Apply(graphicsDevice, CharacterSelectStage.Sway(_swaySeconds));
    }

    private void Apply(GraphicsDevice graphicsDevice, float sway)
    {
        var radians = CharacterSelectStage.CameraAngle * GameUnits.RadiansPerAngle;
        var distance = _distance * _distanceScale;
        var offset = new Vector3(0f, distance * MathF.Sin(radians), distance * MathF.Cos(radians));
        var target = _target + new Vector3(0f, sway, 0f);
        _camera.SetScriptedView(graphicsDevice, target + offset, target, Vector3.Up, CharacterSelectStage.CameraFieldOfView);
    }
}
