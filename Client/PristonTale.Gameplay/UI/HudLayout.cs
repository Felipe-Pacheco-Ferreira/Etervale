using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI;

// The original HUD is authored for 800x600 and offset by (window width - 800, window height - 600):
// bottom-anchored elements move down by the extra height and right-anchored elements move right by the extra width.
// Scale is the largest integer that fits the 800x600 design, times the settings' HUD scale (ClientSettings.UiScale),
// never so large that the design no longer fits.
public readonly record struct HudLayout(float Scale, int Width, int Height, int ViewportWidth, int ViewportHeight)
{
    public const int DesignWidth = 800;
    public const int DesignHeight = 600;

    public static HudLayout Of(Microsoft.Xna.Framework.Graphics.Viewport viewport) => Create(viewport.Width, viewport.Height);

    public static HudLayout Of(Microsoft.Xna.Framework.Graphics.Viewport viewport, float uiScale) => Create(viewport.Width, viewport.Height, uiScale);

    public static HudLayout Create(int viewportWidth, int viewportHeight, float uiScale = 1f)
    {
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        float scale = Math.Max(1, Math.Min(viewportWidth / DesignWidth, viewportHeight / DesignHeight));
        if (uiScale != 1f && float.IsFinite(uiScale) && uiScale > 0f)
        {
            var fits = MathF.Max(1f, MathF.Min((float)viewportWidth / DesignWidth, (float)viewportHeight / DesignHeight));
            scale = MathF.Min(scale * uiScale, MathF.Max(scale, fits));
        }

        return new HudLayout(scale, Math.Max(1, (int)(viewportWidth / scale)), Math.Max(1, (int)(viewportHeight / scale)), viewportWidth, viewportHeight);
    }

    // Whether the HUD is drawn at a whole number of pixels per design pixel (sharp point sampling) or between them.
    public bool WholeScale => Scale == MathF.Floor(Scale);

    public int BottomOffset => Height - DesignHeight;
    public int RightOffset => Width - DesignWidth;
    // The browser's own look of the HUD (the chat, the party and quest windows, the system menu, the action bar's place);
    // off, the HUD is drawn as on the desktop.
    public bool Browser { get; init; }

    // The browser draws its own look unless the player chose the classic interface (ClientSettings.ClassicInterface).
    public static bool BrowserLook(bool browser, ClientSettings settings) => browser && !settings.ClassicInterface;
    public bool SidePanelOpen { get; init; }

    // The page's touch HUD is in use (WebWorldHud.TouchActive): its controls hold the corners the canvas HUD would use.
    public bool Touch { get; init; }

    // Browser only: the bottom bar centred under a wide screen (ClientSettings.CenteredInterface), else at the left.
    public bool CenteredBar { get; init; } = true;

    public int BottomBarOffset => Browser && CenteredBar ? Math.Max(0, RightOffset / 2) : 0;
    public Point BottomBarOrigin => new(BottomBarOffset, BottomOffset);
    public Point ToBottomBarDesign(Point pointer) => pointer - BottomBarOrigin;

    public Rectangle Bottom(Rectangle design) => new(design.X, design.Y + BottomOffset, design.Width, design.Height);
    public Rectangle BottomRight(Rectangle design) => new(design.X + RightOffset, design.Y + BottomOffset, design.Width, design.Height);
    public Point Bottom(int x, int y) => new(x, y + BottomOffset);
    public Point BottomRight(int x, int y) => new(x + RightOffset, y + BottomOffset);

    public Point ToLogical(Point pointer)
    {
        var scale = Scale;
        return new Point((int)MathF.Floor(pointer.X / scale), (int)MathF.Floor(pointer.Y / scale));
    }

    public Rectangle PlaceOverlay(Point preferred, int width, int height)
    {
        var viewportWidth = Math.Max(1, Width);
        var viewportHeight = Math.Max(1, Height);
        var margin = Math.Min(4, (Math.Min(viewportWidth, viewportHeight) - 1) / 2);
        width = Math.Clamp(width, 1, Math.Max(1, viewportWidth - margin * 2));
        height = Math.Clamp(height, 1, Math.Max(1, viewportHeight - margin * 2));
        var x = Math.Clamp(preferred.X, margin, Math.Max(margin, viewportWidth - width - margin));
        var y = Math.Clamp(preferred.Y, margin, Math.Max(margin, viewportHeight - height - margin));
        return new Rectangle(x, y, width, height);
    }

    // value / divisor rounded down, for negative values too.
    internal static int FloorDivide(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
