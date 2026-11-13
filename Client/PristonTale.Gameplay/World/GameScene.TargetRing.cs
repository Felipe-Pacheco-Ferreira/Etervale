using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.World;

// A red ring on the ground under the monster the player attacks, drawn with the blob shadows (a ground quad of the
// skill effects, so the floor's depth hides it like a shadow): it breathes in size and glow and a brighter band turns
// round it, so the target stands out among the monsters around it.
public sealed partial class GameScene
{
    private const int TargetRingPixels = 128;
    private const float TargetRingRadius = 24f;
    // The ring's band, as fractions of its radius, and the turning highlight's share of the circle.
    private const float TargetRingInner = .72f;
    private const float TargetRingOuter = .95f;
    private const float TargetRingHighlight = .18f;

    private Texture2D _targetRing;
    private Texture2D _targetRingHighlight;

    // The entity the ring is drawn under (0 for none); set by the client each frame.
    public ulong GroundRingTarget { get; set; }

    private void QueueTargetRing()
    {
        if (GroundRingTarget == 0 || _skillFx is null || _remote is null || !_remote.TryGetDrawnPosition(GroundRingTarget, out var feet))
        {
            return;
        }

        _targetRing ??= RingTexture(_graphicsDevice, highlight: false);
        _targetRingHighlight ??= RingTexture(_graphicsDevice, highlight: true);
        var seconds = (float)(_skillClockMs / 1000d);
        var breath = .5f + .5f * MathF.Sin(seconds * 5f);
        var radius = TargetRingRadius * (1f + .07f * breath);
        var center = feet + Vector3.Up * 1.5f;
        _skillFx.Renderer.AddGroundQuad(_targetRing, VfxBlend.Additive, center, Vector3.Right * radius, Vector3.Backward * radius,
            new Vector4(1f, .14f, .08f, .65f + .35f * breath));
        var turn = Matrix.CreateRotationY(seconds * 2.4f);
        _skillFx.Renderer.AddGroundQuad(_targetRingHighlight, VfxBlend.Additive, center, Vector3.TransformNormal(Vector3.Right, turn) * radius,
            Vector3.TransformNormal(Vector3.Backward, turn) * radius, new Vector4(1f, .55f, .35f, 1f));
    }

    // The ring's picture: a soft-edged white band (tinted when drawn); with highlight, only a short arc of it, fading out
    // behind its leading end.
    private static Texture2D RingTexture(GraphicsDevice device, bool highlight)
    {
        var pixels = new Color[TargetRingPixels * TargetRingPixels];
        var half = TargetRingPixels / 2f;
        var middle = (TargetRingInner + TargetRingOuter) / 2f;
        var width = (TargetRingOuter - TargetRingInner) / 2f;
        for (var y = 0; y < TargetRingPixels; y++)
        {
            for (var x = 0; x < TargetRingPixels; x++)
            {
                var dx = (x + .5f - half) / half;
                var dy = (y + .5f - half) / half;
                var band = 1f - MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - middle) / width;
                var alpha = Math.Clamp(band, 0f, 1f);
                alpha *= alpha;
                if (highlight)
                {
                    var turn = (MathF.Atan2(dy, dx) / MathF.Tau + 1f) % 1f;
                    alpha *= turn < TargetRingHighlight ? turn / TargetRingHighlight : 0f;
                }

                pixels[y * TargetRingPixels + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        var texture = new Texture2D(device, TargetRingPixels, TargetRingPixels);
        texture.SetData(pixels);
        return texture;
    }
}
