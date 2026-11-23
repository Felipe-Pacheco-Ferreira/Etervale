using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

// Where the touch HUD's frames leave their round windows, in fractions of the screen's short side (the Android client's
// portraits): the player's from the top-left corner; the target's at the left end of its frame, which is centred at the
// top. The page draws the frames around them (the touch state hands these out) and the game draws the 3D heads inside;
// shift is where the HUD editor moved the frame, in the same fractions.
internal static class TouchPortraitPlacement
{
    public const float X = .035f;
    public const float Y = .035f;
    public const float Size = .15f;

    public const float TargetFrameWidth = .7f;
    public const float TargetFrameTop = .015f;
    public const float TargetInset = .02f;
    public const float TargetY = .03f;
    public const float TargetSize = .11f;

    // The farthest the editor may move a frame, in short sides: the page keeps the frames on the screen within it.
    public const float MaxShift = 2f;

    public static Rectangle Bounds(int width, int height, Vector2 shift)
    {
        var side = Math.Min(width, height);
        return new Rectangle((int)((X + shift.X) * side), (int)((Y + shift.Y) * side), (int)(Size * side), (int)(Size * side));
    }

    public static Rectangle TargetBounds(int width, int height, Vector2 shift)
    {
        var side = Math.Min(width, height);
        var left = width / 2f - TargetFrameWidth / 2f * side + (TargetInset + shift.X) * side;
        return new Rectangle((int)left, (int)((TargetY + shift.Y) * side), (int)(TargetSize * side), (int)(TargetSize * side));
    }

    // A shift the page reported, kept finite and within MaxShift.
    public static Vector2 Shift(float x, float y) =>
        new(float.IsFinite(x) ? Math.Clamp(x, -MaxShift, MaxShift) : 0f, float.IsFinite(y) ? Math.Clamp(y, -MaxShift, MaxShift) : 0f);
}
