// Where the HUD's two portraits sit, in fractions of the screen height: the game draws the 3D heads there
// (RicartenProbe.Draw) and the Android frames over them leave a round window (VulkanPlayerFrame, VulkanTargetFrame).
// The back buffer keeps the screen's aspect, so a fraction of its height lands on the same fraction of the screen's.
// When the player moves a frame (HudLayoutEditor), its shift is set here from the UI thread and read by the game thread.
internal static class HudPlacement
{
    // The player's portrait, from the top-left corner.
    public const float PlayerPortraitX = .035f;
    public const float PlayerPortraitY = .035f;
    public const float PlayerPortraitSize = .15f;

    // The target frame, centred at the top; its portrait sits at its left end.
    public const float TargetFrameWidth = .7f;
    public const float TargetFrameTop = .015f;
    public const float TargetPortraitInset = .02f;
    public const float TargetPortraitY = .03f;
    public const float TargetPortraitSize = .11f;

    private static float playerShiftX;
    private static float playerShiftY;
    private static float targetShiftX;
    private static float targetShiftY;

    // How far the player moved each frame from its place, in fractions of the screen height.
    public static float PlayerShiftX => Volatile.Read(ref playerShiftX);
    public static float PlayerShiftY => Volatile.Read(ref playerShiftY);
    public static float TargetShiftX => Volatile.Read(ref targetShiftX);
    public static float TargetShiftY => Volatile.Read(ref targetShiftY);

    public static void ShiftPlayer(float x, float y)
    {
        Volatile.Write(ref playerShiftX, x);
        Volatile.Write(ref playerShiftY, y);
    }

    public static void ShiftTarget(float x, float y)
    {
        Volatile.Write(ref targetShiftX, x);
        Volatile.Write(ref targetShiftY, y);
    }

    // The target portrait's left edge, in pixels, on a screen of this width and height, before any shift.
    public static float TargetPortraitLeft(float width, float height) => width / 2f - TargetFrameWidth / 2f * height + TargetPortraitInset * height;
}
