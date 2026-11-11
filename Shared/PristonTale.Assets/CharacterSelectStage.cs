using PristonTale.Contracts.Characters;

namespace PristonTale.Assets;

// How the character selection is staged on the login stage: where characters stand, how the camera travels and how
// the chosen class walks forward. Planar positions are the stage's own (x, z); the renderers mirror z. The desktop
// screen and the Android one both read these values.
public static class CharacterSelectStage
{
    public const string StagePath = "cSelect/Login.smd";

    // The screen's steps, in the order the camera rail and the title rows follow.
    public const int RosterStep = 0;
    public const int TribeStep = 1;
    public const int ClassStep = 2;
    public const int FaceStep = 3;
    public const int NameStep = 4;

    private static readonly (float X, float Z)[] RosterPositions =
    [
        (0f, -110f),
        (-35f, -90f),
        (35f, -90f),
        (-70f, -60f),
        (70f, -60f),
    ];

    private static readonly (float X, float Z)[] FullRosterPositions =
    [
        (-18f, -110f),
        (18f, -110f),
        (-53f, -85f),
        (53f, -85f),
        (-88f, -55f),
        (88f, -55f),
    ];

    // Where the green flames burn, in render space.
    public static IReadOnlyList<(float X, float Y, float Z)> Braziers { get; } =
    [
        (-70f, 55f, 156f), (-77f, 60f, 143f), (-81f, 65f, 143f),
        (70f, 56f, 155f), (75f, 60f, 152f), (81f, 66f, 142f),
    ];

    public static (float X, float Z) RosterPosition(int characterCount, int index) =>
        (characterCount == CharacterSlots.Maximum ? FullRosterPositions : RosterPositions)[index];

    // The class lineup stands from -90 to 90: five classes 45 apart, six 36 apart; the two at the ends stand 10 nearer.
    public static (float X, float Z) LineupPosition(int index, int count)
    {
        var spacing = count > 5 ? 36 : 45;
        return ((2 * index - (count - 1)) * spacing / 2, index == 0 || index == count - 1 ? -320f : -330f);
    }

    // A character is picked inside a box from its feet to this height, this many pixels to each side of its feet.
    public const float PickHeight = 45f;
    public const float PickHalfWidthPixels = 26f;

    // The floor under a slot when the stage gives none within PlausibleFloor of zero.
    public const float FloorY = 0f;
    public const float PlausibleFloor = 40f;

    // The chosen class runs to ForwardZ, stops once past StopZ and rests at RestZ, turning at TurnRate radians a second.
    public const float ForwardZ = -420f;
    public const float StopZ = -415f;
    public const float RestZ = -429f;
    public const float RunUnitsPerSecond = 120f;
    public const float TurnRate = 9f;
    public const float YawSettle = 0.01f;

    // Colour added to a character: the selected one, the one under the pointer.
    public const float SelectedGlow = 120f / 255f;
    public const float HoverGlow = 50f / 255f;

    // Two presses on the same character within this many seconds enter or continue.
    public const double DoublePressSeconds = 0.35d;

    // The camera looks at (0, TargetY, 0) from RailDistance(step) away, CameraAngle angle units above the horizon.
    public const float CameraTargetY = 16f;
    public const int CameraAngle = 90;
    public const float CameraFieldOfView = MathF.PI / 4.4f;
    public const float DollyFastPerSecond = 235f;
    public const float DollySlowPerSecond = 23f;
    public const float DollySnap = 3.1f;
    public const float SwayAmplitude = 1.25f;
    public const float SwaySeconds = 160f / 60f;

    private static readonly float[] Rail = [300f, 370f, 550f, 720f, 700f];

    public static float RailDistance(int step) => Rail[step];

    // The brazier flames: FlameRate a second rise at FlameRise units a second, leaning up to FlameLean sideways, from
    // FlameShell units around the brazier; FlameSize across growing FlameGrowth a second, the flipbook at FlameRate frames
    // a second, fading out over their last FlameFadeOut seconds of a life between the two bounds.
    public const float FlameRate = 9f;
    public const float FlameRise = 60f;
    public const float FlameLean = FlameRise * 0.06f;
    public const float FlameShell = 4f;
    public const float FlameSize = 24f;
    public const float FlameGrowth = 10f / 2.1f;
    public const float FlameShortestLife = 1.8f;
    public const float FlameLongestLife = 2.4f;
    public const float FlameFadeOut = 2.4f;

    // Red, green, blue and opacity out of 255.
    public static (float R, float G, float B, float A) FlameColor => (80f, 235f, 110f, 115f);

    // The interior ambient the stage's characters are lit by.
    public static (float R, float G, float B) Ambient => (0.85f, 0.82f, 0.78f);

    public static (byte R, byte G, byte B) ClearColor => (8, 7, 10);

    // The face portrait's camera: this vertical field of view, framing VisibleHeight of the character's height around
    // the point FocusHeight up it.
    public const float PortraitFieldOfView = 30f * MathF.PI / 180f;
    public const float PortraitVisibleHeight = 0.66f;
    public const float PortraitFocusHeight = 0.70f;

    public static float Glow(int index, int hoveredIndex, int selectedIndex)
    {
        if (index == selectedIndex)
        {
            return SelectedGlow;
        }

        return index == hoveredIndex ? HoverGlow : 0f;
    }

    // -1 to 1 and back over one period.
    public static float Sway(float seconds)
    {
        var phase = seconds % SwaySeconds / SwaySeconds;
        return (phase < 0.5f ? phase * 4f - 1f : 3f - phase * 4f) * SwayAmplitude;
    }

    // The rail distance after dt seconds on the way from one step to another, and the step reached.
    public static (float Distance, int Step) Dolly(float distance, int currentStep, int targetStep, float dt)
    {
        if (targetStep == currentStep)
        {
            return (RailDistance(targetStep), currentStep);
        }

        var from = RailDistance(currentStep);
        var to = RailDistance(targetStep);
        var remaining = MathF.Abs(to - distance);
        var step = (remaining < MathF.Abs(to - from) / 16f ? DollySlowPerSecond : DollyFastPerSecond) * dt;
        if (targetStep > currentStep)
        {
            distance += step;
            return distance > to - DollySnap ? (to, targetStep) : (distance, currentStep);
        }

        distance -= step;
        return distance < to + DollySnap ? (to, targetStep) : (distance, currentStep);
    }
}
