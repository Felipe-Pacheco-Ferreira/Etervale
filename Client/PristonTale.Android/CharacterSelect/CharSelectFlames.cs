using System.Numerics;
using PristonTale.Assets;

namespace PristonTale.Mobile.CharacterSelect;

// The green flames over the braziers: the desktop effect (CharSelectEffects, vfx.charselect.beam) as a steady stream of
// camera-facing flames with the numbers of CharacterSelectStage.
internal sealed class CharSelectFlames
{
    private const float Rate = CharacterSelectStage.FlameRate;
    private const float Rise = CharacterSelectStage.FlameRise;
    private const float Lean = CharacterSelectStage.FlameLean;
    private const float Shell = CharacterSelectStage.FlameShell;
    private const float Size = CharacterSelectStage.FlameSize;
    private const float Growth = CharacterSelectStage.FlameGrowth;
    private const float ShortestLife = CharacterSelectStage.FlameShortestLife;
    private const float LongestLife = CharacterSelectStage.FlameLongestLife;
    private const float FadeOut = CharacterSelectStage.FlameFadeOut;
    private static readonly Vector4 Tint = new Vector4(CharacterSelectStage.FlameColor.R, CharacterSelectStage.FlameColor.G, CharacterSelectStage.FlameColor.B,
        CharacterSelectStage.FlameColor.A) / byte.MaxValue;

    private struct Flame
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Age;
        public float Life;
    }

    private readonly Flame[] _flames;
    private readonly Random _random = new();
    private int _count;
    private float _owed = 1f;

    public CharSelectFlames()
    {
        _flames = new Flame[(int)MathF.Ceiling(Rate * LongestLife + 1f) * CharacterSelectStage.Braziers.Count];
    }

    public void Update(float dt)
    {
        for (var index = _count - 1; index >= 0; index--)
        {
            ref var flame = ref _flames[index];
            flame.Age += dt;
            if (flame.Age >= flame.Life)
            {
                _flames[index] = _flames[--_count];
                continue;
            }

            flame.Position += flame.Velocity * dt;
        }

        _owed += dt * Rate;
        while (_owed >= 1f)
        {
            _owed -= 1f;
            for (var brazier = 0; brazier < CharacterSelectStage.Braziers.Count; brazier++)
            {
                var (x, y, z) = CharacterSelectStage.Braziers[brazier];
                Spawn(new Vector3(x, y, z));
            }
        }
    }

    private void Spawn(Vector3 brazier)
    {
        if (_count == _flames.Length)
        {
            return;
        }

        _flames[_count++] = new Flame
        {
            Position = brazier + RandomDirection() * Shell,
            Velocity = new Vector3(Range(-Lean, Lean), Rise, Range(-Lean, Lean)),
            Life = Range(ShortestLife, LongestLife)
        };
    }

    public void Draw(CharSelectGraphics graphics, CharSelectCamera camera)
    {
        graphics.BeginAdditive();
        var viewProjection = camera.ViewProjection;
        for (var index = 0; index < _count; index++)
        {
            ref readonly var flame = ref _flames[index];
            var frame = (int)(flame.Age * Rate) % CharacterSelectArt.FlameFrames;
            var fade = Math.Clamp((flame.Life - flame.Age) / FadeOut, 0f, 1f);
            graphics.DrawBillboard(graphics.Art(FrameNames[frame]), flame.Position, Size + Growth * flame.Age, camera.Right, camera.Up, viewProjection,
                Tint with { W = Tint.W * fade });
        }
    }

    private static readonly string[] FrameNames = [.. Enumerable.Range(1, CharacterSelectArt.FlameFrames).Select(CharacterSelectArt.FlameFrame)];

    private float Range(float minimum, float maximum) => minimum + (maximum - minimum) * _random.NextSingle();

    private Vector3 RandomDirection()
    {
        var height = Range(-1f, 1f);
        var angle = Range(0f, MathF.Tau);
        var radius = MathF.Sqrt(1f - height * height);
        return new Vector3(radius * MathF.Cos(angle), height, radius * MathF.Sin(angle));
    }
}
