using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D;
using PristonTale.Gameplay.UI.Duels;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.World;

// The local player's duel (DuelRules): the answer to a challenge, and the arena's edge drawn on the ground as a thin gold
// ring with the target ring's ground quads, a turning brighter arc on it.
public sealed partial class GameScene : IDuelClient
{
    private const int DuelRingPixels = 512;
    private const float DuelRingInner = .975f;
    private const float DuelRingOuter = .995f;

    private Texture2D _duelRing;
    private Texture2D _duelRingHighlight;

    // The opponent (0 outside a duel) and the arena's center in server space while the local player duels, else null; set
    // by the client each frame.
    public ulong DuelOpponent { get; set; }

    public System.Numerics.Vector3? DuelArena { get; set; }

    public Task AnswerDuelAsync(ulong fromId, bool accept) => SendSocial(_playChannel?.AnswerDuelAsync(fromId, accept, _presenceCts.Token), "duel answer");

    private void QueueDuelRing()
    {
        if (DuelArena is not { } arena || _skillFx is null)
        {
            return;
        }

        _duelRing ??= RingTexture(_graphicsDevice, DuelRingPixels, DuelRingInner, DuelRingOuter, highlight: false);
        _duelRingHighlight ??= RingTexture(_graphicsDevice, DuelRingPixels, DuelRingInner, DuelRingOuter, highlight: true);
        var mirrored = WorldSpace.Mirror(arena);
        var center = new Vector3(mirrored.X, mirrored.Y + 2f, mirrored.Z);
        var radius = DuelRules.ArenaRadius;
        var seconds = (float)(_skillClockMs / 1000d);
        _skillFx.Renderer.AddGroundQuad(_duelRing, VfxBlend.Additive, center, Vector3.Right * radius, Vector3.Backward * radius,
            new Vector4(1f, .78f, .3f, .85f));
        var turn = Matrix.CreateRotationY(seconds * .8f);
        _skillFx.Renderer.AddGroundQuad(_duelRingHighlight, VfxBlend.Additive, center, Vector3.TransformNormal(Vector3.Right, turn) * radius,
            Vector3.TransformNormal(Vector3.Backward, turn) * radius, new Vector4(1f, .95f, .7f, 1f));
    }
}
