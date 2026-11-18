namespace PristonTale.Gameplay.UI;

internal static class WebWorldHud
{
    internal static bool Enabled { get; set; }
    internal static bool HasCanvasPointer { get; set; }
    internal static Action OpenSettings { get; set; }
    internal static GameplayHud Current { get; private set; }

    internal static void Attach(GameplayHud hud)
    {
        if (Enabled)
        {
            Current = hud;
        }
    }

    internal static void Detach(GameplayHud hud)
    {
        if (Current == hud)
        {
            Current = null;
        }
    }
}
