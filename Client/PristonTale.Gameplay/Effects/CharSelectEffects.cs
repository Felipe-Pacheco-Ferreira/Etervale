using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// The green flames over the character-select braziers: nine a second rise at 60 units a second within 0.06 rad of the
// vertical from 4 units around the brazier, the 8-frame flame flipbook at 9 frames a second, growing from 24 to 34
// units across and fading out over their 1.8 to 2.4 s. Sizes are full extents.
public static class CharSelectEffects
{
    public const string GreenBeam = "vfx.charselect.beam";
    // Seconds of flames one beam effect emits; CharSelectFlames starts the next as it ends, so the flames never stop.
    public const float BeamPeriod = 2f;

    private const float Rise = CharacterSelectStage.FlameRise;
    private const float Lean = CharacterSelectStage.FlameLean;
    private const float Rate = CharacterSelectStage.FlameRate;

    public static VfxDefinition GreenBeamDefinition() => new()
    {
        Name = GreenBeam,
        Duration = BeamPeriod + CharacterSelectStage.FlameLongestLife,
        Parts =
        [
            new VfxPartDefinition
            {
                Count = (int)(BeamPeriod * Rate),
                DelayStep = 1f / Rate,
                Lifetime = [CharacterSelectStage.FlameShortestLife, CharacterSelectStage.FlameLongestLife],
                Texture = CharacterSelectArt.FlameTexture,
                Frames = CharacterSelectArt.FlameFrames,
                FrameTime = 1f / Rate,
                Loop = true,
                Color = [CharacterSelectStage.FlameColor.R, CharacterSelectStage.FlameColor.G, CharacterSelectStage.FlameColor.B, CharacterSelectStage.FlameColor.A],
                Size = [CharacterSelectStage.FlameSize, CharacterSelectStage.FlameSize],
                Growth = CharacterSelectStage.FlameGrowth,
                Fade = VfxPartFade.InOut,
                FadeOut = CharacterSelectStage.FlameFadeOut,
                Shell = [CharacterSelectStage.FlameShell, CharacterSelectStage.FlameShell, CharacterSelectStage.FlameShell],
                VelocityMin = [-Lean, Rise, -Lean],
                VelocityMax = [Lean, Rise, Lean]
            }
        ]
    };
}

// Keeps the flames burning: a beam effect at every brazier each BeamPeriod seconds, the first on the first update.
public sealed class CharSelectFlames(VfxSystem system, CompiledVfx beam, IReadOnlyList<Vector3> braziers)
{
    private float _clock = CharSelectEffects.BeamPeriod;

    public void Update(float seconds)
    {
        system.Update(seconds);
        _clock += seconds;
        while (_clock >= CharSelectEffects.BeamPeriod)
        {
            _clock -= CharSelectEffects.BeamPeriod;
            foreach (var brazier in braziers)
            {
                system.Spawn(beam, new VfxSpawn(0, 0, brazier, 0f));
            }
        }
    }
}
