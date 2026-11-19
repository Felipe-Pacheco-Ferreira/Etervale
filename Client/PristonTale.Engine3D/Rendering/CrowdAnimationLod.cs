namespace PristonTale.Engine3D.Rendering;

// How often an actor of a crowd samples its skeleton again, the costly part of animating it, at the High character
// quality: every frame within NearDistance of the camera's focus (on screen or not, so the shadows near the player keep
// up), every second frame to FarDistance, every fourth beyond, and every eighth while off screen. Each actor's turn is
// staggered by its slot so the samples spread over frames; its world, and so its place on screen, still follows every
// frame, and a new motion is sampled at once. The target frame's portrait shows its actor close up, so that actor
// samples every frame wherever it stands. The other character qualities change the distances and intervals (Levels).
public static class CrowdAnimationLod
{
    public const float NearDistance = 400f;
    public const float FarDistance = 900f;
    public const int FarInterval = 4;
    public const int OffScreenInterval = 8;

    // Indexed by CharacterQuality: Very high samples every actor on screen every frame; Medium shortens the distances;
    // Low also doubles the intervals.
    private static readonly CrowdLodLevel[] Levels =
    [
        new(NearDistance, float.MaxValue, 1, 1, OffScreenInterval),
        new(NearDistance, FarDistance, 2, FarInterval, OffScreenInterval),
        new(250f, 600f, 2, FarInterval, OffScreenInterval),
        new(250f, 600f, 4, FarInterval * 2, OffScreenInterval * 2)
    ];

    public static CrowdLodLevel LevelOf(CharacterQuality quality) => Levels[(int)quality];

    public static int Interval(float distance, bool onScreen, bool portrait = false, CharacterQuality quality = CharacterQuality.High)
    {
        var level = LevelOf(quality);
        if (portrait || distance <= level.NearDistance)
        {
            return 1;
        }

        return !onScreen ? level.OffScreenInterval : distance <= level.FarDistance ? level.MiddleInterval : level.FarInterval;
    }

    public static bool Due(int interval, uint frame, int slot) => interval <= 1 || (frame + (uint)slot) % (uint)interval == 0;
}

public readonly record struct CrowdLodLevel(float NearDistance, float FarDistance, int MiddleInterval, int FarInterval, int OffScreenInterval);
