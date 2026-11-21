namespace PristonTale.Gameplay.UI;

internal static class WebWorldHud
{
    internal static bool Enabled { get; set; }
    internal static bool HasCanvasPointer { get; set; }
    internal static Action OpenSettings { get; set; }
    internal static GameplayHud Current { get; private set; }
    // The page's fingers on the canvas, and whether the device has no fine pointer (a phone or tablet): with the
    // interface type, they decide the touch HUD (ClientSettings.UsesTouch).
    internal static Input.ITouchFingers Fingers { get; set; }
    internal static bool TouchDevice { get; set; }

    // Whether the page's touch HUD is in use: the page reports fingers and the interface type asks for it.
    internal static bool TouchActive(Engine3D.Rendering.ClientSettings settings) => Fingers is not null && settings.UsesTouch(TouchDevice);

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
