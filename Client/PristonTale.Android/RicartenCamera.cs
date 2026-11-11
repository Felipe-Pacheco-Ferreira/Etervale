using System.Numerics;

namespace PristonTale.Mobile;

internal sealed record RicartenCamera(float Yaw = 0f, float Pitch = .685f, float Distance = 264f)
{
    private Vector2 Forward => new(MathF.Sin(Yaw), MathF.Cos(Yaw));

    public RicartenCamera Drag(Vector2 delta) => new(
        MathF.IEEERemainder(Yaw - delta.X * MathF.Tau, MathF.Tau),
        Math.Clamp(Pitch + delta.Y * MathF.PI, .28f, 1.25f), Distance);

    public RicartenCamera Zoom(float scale) => this with { Distance = Math.Clamp(Distance / scale, 155f, 620f) };

    public Vector2 MovementDirection(Vector2 stick)
    {
        var forward = Forward;
        var right = new Vector2(-forward.Y, forward.X);
        return right * stick.X + forward * stick.Y;
    }

    public Matrix4x4 ViewProjection(Vector3 player, float aspect)
    {
        var forward = new Vector3(Forward.X, 0f, Forward.Y);
        var focus = player + 22f * Vector3.UnitY + 12f * forward;
        var offset = Distance * (MathF.Sin(Pitch) * Vector3.UnitY - MathF.Cos(Pitch) * forward);
        return Matrix4x4.CreateLookAt(focus + offset, focus, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(.68f, aspect, 8f, 2500f);
    }
}