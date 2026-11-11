namespace PristonTale.Assets;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

public enum CharacterSelectButton
{
    Select,
    Back,
    NewCharacter,
    Exit,
    Delete
}

// Where the character selection's 2D art sits in a viewport of the given pixel size. The desktop screen passes its
// window; the Android screen passes a virtual viewport DesignHeight high so the art keeps the desktop's proportions.
public static class CharacterSelectLayout
{
    public const int DesignWidth = 800;
    public const int DesignHeight = 600;

    public static int ButtonRowY(int height) => height - 40;
    public static int TwoButtonPrimaryX(int width) => width / 2 - 120;
    public static int TwoButtonQuitX(int width) => width / 2 - 120 + 110;
    public const int TwoButtonDeleteX = 0;
    public static int ThreeButtonOkX(int width) => width / 2 - 150;
    public static int ThreeButtonBackX(int width) => width / 2 - 150 + 100;
    public static int ThreeButtonQuitX(int width) => width / 2 - 150 + 200;

    public static ScreenRect NewCharPrompt(int width) => new(width / 2 - 128, 150, 256, 64);
    public static ScreenRect NewCharHotspot(int width, int height) => new(width / 2 - 128, height / 4 - 80, 256, 210);

    public static ScreenRect TempskronMark(int width, int height) => new(width / 2 - 140, height / 5, 128, 128);
    public static ScreenRect MorionMark(int width, int height) => new(width / 2 + 12, height / 5, 128, 128);

    public static ScreenRect FaceBackdropTop(int width, int height) => Scaled(width, height, 272f, 82f, 256f, 256f);
    public static ScreenRect FaceBackdropBottom(int width, int height) => Scaled(width, height, 272f, 338f, 256f, 128f);
    public static ScreenRect FacePortrait(int width, int height) => Scaled(width, height, 288f, 102f, 224f, 286f);

    public static (float X, float Y) FaceInputPosition(int width, int height)
    {
        var (scale, offsetX, offsetY) = FaceCanvas(width, height);
        return (offsetX + 358f * scale, offsetY + 398f * scale);
    }

    public static int FaceNameDecorationY(int height) => height - 66;

    public static ScreenRect FaceThumbHit(int width, int height, int index)
    {
        const int boxWidth = 128;
        return new ScreenRect(width / 2 - 192 + index * boxWidth, height - (70 + 128), boxWidth, boxWidth);
    }

    public static ScreenRect FaceThumbDraw(int width, int height, int index)
    {
        var hit = FaceThumbHit(width, height, index);
        return hit with { Y = hit.Y + 20 };
    }

    // The part of a button's art that answers a press: its drawn width has empty margins.
    public static ScreenRect ButtonHit(int textureHeight, int x, int y, CharacterSelectButton kind)
    {
        var (left, right) = kind switch
        {
            CharacterSelectButton.Select => (10, 110),
            CharacterSelectButton.Back => (20, 103),
            CharacterSelectButton.NewCharacter => (10, 110),
            CharacterSelectButton.Exit => (15, 108),
            CharacterSelectButton.Delete => (10, 108),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        return new ScreenRect(x + left, y, right - left, textureHeight);
    }

    // The face step's art is laid out on the design canvas, scaled to fit and centred.
    private static (float Scale, float OffsetX, float OffsetY) FaceCanvas(int width, int height)
    {
        var scale = MathF.Min(width / (float)DesignWidth, height / (float)DesignHeight);
        return (scale, (width - DesignWidth * scale) * 0.5f, (height - DesignHeight * scale) * 0.5f);
    }

    private static ScreenRect Scaled(int viewportWidth, int viewportHeight, float x, float y, float width, float height)
    {
        var (scale, offsetX, offsetY) = FaceCanvas(viewportWidth, viewportHeight);
        return new ScreenRect(
            (int)MathF.Round(offsetX + x * scale),
            (int)MathF.Round(offsetY + y * scale),
            (int)MathF.Round(width * scale),
            (int)MathF.Round(height * scale));
    }
}
