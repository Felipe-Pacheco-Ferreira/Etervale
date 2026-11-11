using System.Numerics;
using PristonTale.Assets;
using PristonTale.Contracts.Units;
using Matrix = System.Numerics.Matrix4x4;

namespace PristonTale.Mobile.CharacterSelect;

// The desktop CharacterSelectCamera: on the rail of CharacterSelectStage, dollying between the steps' distances and
// swaying. Render space is the desktop's (right-handed, the stage's z mirrored), so the matrices match it.
internal sealed class CharSelectCamera
{
    private const float Near = 20f;
    private const float Far = 4000f;

    private float _distance;
    private int _currentStep;
    private float _swaySeconds;

    public CharSelectCamera(int initialStep)
    {
        _currentStep = initialStep;
        _distance = CharacterSelectStage.RailDistance(initialStep);
        Apply(0f, 1f);
    }

    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }
    public Matrix ViewProjection { get; private set; }
    public Vector3 Right { get; private set; }
    public Vector3 Up { get; private set; }

    public void Update(int targetStep, float dt, float aspect)
    {
        (_distance, _currentStep) = CharacterSelectStage.Dolly(_distance, _currentStep, targetStep, dt);
        _swaySeconds += dt;
        Apply(CharacterSelectStage.Sway(_swaySeconds), aspect);
    }

    private void Apply(float sway, float aspect)
    {
        var radians = CharacterSelectStage.CameraAngle * GameUnits.RadiansPerAngle;
        var target = new Vector3(0f, CharacterSelectStage.CameraTargetY + sway, 0f);
        var position = target + new Vector3(0f, _distance * MathF.Sin(radians), _distance * MathF.Cos(radians));
        View = Matrix.CreateLookAt(position, target, Vector3.UnitY);
        Projection = Matrix.CreatePerspectiveFieldOfView(CharacterSelectStage.CameraFieldOfView, aspect, Near, Far);
        ViewProjection = View * Projection;
        Right = new Vector3(View.M11, View.M21, View.M31);
        Up = new Vector3(View.M12, View.M22, View.M32);
    }

    // A render-space point in viewport pixels (x right, y down) with its depth, 0 at the near plane and 1 at the far.
    public Vector3 Project(Vector3 point, int viewportWidth, int viewportHeight)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), ViewProjection);
        if (clip.W <= 0f)
        {
            return new Vector3(0f, 0f, -1f);
        }

        return new Vector3((clip.X / clip.W + 1f) * 0.5f * viewportWidth, (1f - clip.Y / clip.W) * 0.5f * viewportHeight, clip.Z / clip.W);
    }
}
