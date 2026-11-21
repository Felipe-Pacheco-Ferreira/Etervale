using System.Runtime.InteropServices.JavaScript;
using PristonTale.Gameplay.UI;

namespace PristonTale.Browser;

// The touch HUD (wwwroot/touch-hud.js): the page reports the canvas's fingers and whether the device is touch-only,
// asks whether the touch HUD is in use (the interface type in Settings) and reads its buttons' state.
public static partial class BrowserHost
{
    private static readonly BrowserTouchFingers Fingers = new();

    [JSExport]
    public static void SetTouchDevice(bool touchDevice)
    {
        WebWorldHud.TouchDevice = touchDevice;
        WebWorldHud.Fingers = Fingers;
    }

    [JSExport]
    public static void ReportTouch(int id, double x, double y, bool down) => Fingers.Report(id, (float)x, (float)y, down);

    [JSExport]
    public static void ClearTouches() => Fingers.Clear();

    [JSExport]
    public static bool TouchInterface() => Current.UsesTouch(WebWorldHud.TouchDevice);

    [JSExport]
    public static string TouchState() => WebWorldHud.Current?.TouchState() ?? string.Empty;

    // The newest chat lines for the touch HUD's chat (GameplayHud.TouchChat) and a line its chat window sends.
    [JSExport]
    public static string TouchChat(int lines) => WebWorldHud.Current?.TouchChat(lines) ?? string.Empty;

    [JSExport]
    public static void TouchChatSend(string text) => WebWorldHud.Current?.TouchChatSend(text);

    // A skill button's or belt potion's picture named in TouchState, decoded (GameplayHud.TouchPicture).
    [JSExport]
    public static byte[] TouchPicture(string path) => WebWorldHud.Current?.TouchPicture(path) ?? [];
}
