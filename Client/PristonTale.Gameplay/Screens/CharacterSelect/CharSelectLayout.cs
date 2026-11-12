using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

// The shared layout (CharacterSelectLayout) in the window's viewport, as MonoGame rectangles.
internal static class CharSelectLayout
{
    public const int FacesPerJob = CharacterFace.FaceCount;

    public static int ButtonRowY(Viewport vp) => CharacterSelectLayout.ButtonRowY(vp.Height);
    public static int TwoButtonPrimaryX(Viewport vp) => CharacterSelectLayout.TwoButtonPrimaryX(vp.Width);
    public static int TwoButtonQuitX(Viewport vp) => CharacterSelectLayout.TwoButtonQuitX(vp.Width);
    public const int TwoButtonDeleteX = CharacterSelectLayout.TwoButtonDeleteX;
    public static int ThreeButtonOkX(Viewport vp) => CharacterSelectLayout.ThreeButtonOkX(vp.Width);
    public static int ThreeButtonBackX(Viewport vp) => CharacterSelectLayout.ThreeButtonBackX(vp.Width);
    public static int ThreeButtonQuitX(Viewport vp) => CharacterSelectLayout.ThreeButtonQuitX(vp.Width);

    public static Rectangle NewCharPrompt(Viewport vp) => Rect(CharacterSelectLayout.NewCharPrompt(vp.Width));
    public static Rectangle NewCharHotspot(Viewport vp) => Rect(CharacterSelectLayout.NewCharHotspot(vp.Width, vp.Height));

    public static Rectangle TempscronMark(Viewport vp) => Rect(CharacterSelectLayout.TempskronMark(vp.Width, vp.Height));
    public static Rectangle MorionMark(Viewport vp) => Rect(CharacterSelectLayout.MorionMark(vp.Width, vp.Height));

    public static Rectangle FaceBackdropTop(Viewport vp) => Rect(CharacterSelectLayout.FaceBackdropTop(vp.Width, vp.Height));
    public static Rectangle FaceBackdropBottom(Viewport vp) => Rect(CharacterSelectLayout.FaceBackdropBottom(vp.Width, vp.Height));
    public static Rectangle FacePortrait(Viewport vp) => Rect(CharacterSelectLayout.FacePortrait(vp.Width, vp.Height));

    public static Vector2 FaceInputPosition(Viewport vp)
    {
        var (x, y) = CharacterSelectLayout.FaceInputPosition(vp.Width, vp.Height);
        return new Vector2(x, y);
    }

    public static int FaceNameDecorationY(Viewport vp) => CharacterSelectLayout.FaceNameDecorationY(vp.Height);

    public static Rectangle FaceThumbHit(Viewport vp, int index) => Rect(CharacterSelectLayout.FaceThumbHit(vp.Width, vp.Height, index));
    public static Rectangle FaceThumbDraw(Viewport vp, int index) => Rect(CharacterSelectLayout.FaceThumbDraw(vp.Width, vp.Height, index));

    public static Rectangle ButtonHit(Texture2D texture, int x, int y, CharacterSelectButton kind) =>
        Rect(CharacterSelectLayout.ButtonHit(texture.Height, x, y, kind));

    public static Rectangle ScaleButton(Rectangle rect, Viewport viewport, float scale, bool leftAligned)
    {
        var anchorX = leftAligned ? 0 : viewport.Width / 2;
        return new Rectangle(anchorX + (int)MathF.Round((rect.X - anchorX) * scale),
            viewport.Height + (int)MathF.Round((rect.Y - viewport.Height) * scale),
            (int)MathF.Round(rect.Width * scale), (int)MathF.Round(rect.Height * scale));
    }

    private static Rectangle Rect(ScreenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
