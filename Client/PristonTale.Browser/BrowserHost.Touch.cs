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

    // The quest card's state and, with journal, the journal's entries (GameplayHud.TouchQuest).
    [JSExport]
    public static string TouchQuest(bool journal) => WebWorldHud.Current?.TouchQuest(journal) ?? string.Empty;

    // How many times the minimap was tapped and, with full, the field map (GameplayHud.TouchMap).
    [JSExport]
    public static string TouchMap(bool full) => WebWorldHud.Current?.TouchMap(full) ?? string.Empty;

    // How many times the bag was asked for and, with full, the bag and the chosen item's sheet (GameplayHud.TouchBag).
    [JSExport]
    public static string TouchBag(bool full, string chosen) => WebWorldHud.Current?.TouchBag(full, chosen) ?? string.Empty;

    // How many times the status window was asked for and, with full, the character and what planned points add
    // (GameplayHud.TouchStatus).
    [JSExport]
    public static string TouchStatus(bool full, string planned) => WebWorldHud.Current?.TouchStatus(full, planned) ?? string.Empty;

    // How many times the skills window was asked for and, with full, a tier's skills and the chosen one's sheet
    // (GameplayHud.TouchSkills).
    [JSExport]
    public static string TouchSkills(bool full, int tier, int chosen) => WebWorldHud.Current?.TouchSkills(full, tier, chosen) ?? string.Empty;

    // The skills a touch skill button may take, for the list a long press on it opens (GameplayHud.TouchSkillChoices).
    [JSExport]
    public static string TouchSkillChoices() => WebWorldHud.Current?.TouchSkillChoices() ?? string.Empty;

    // Where the HUD editor moved the player and target frames, in short sides, so the 3D heads follow them.
    [JSExport]
    public static void SetTouchFrameShift(float playerX, float playerY, float targetX, float targetY) =>
        WebWorldHud.Current?.SetTouchFrameShift(playerX, playerY, targetX, targetY);

    // What the tutorials need to start a lesson and move its steps on (GameplayHud.TouchTutorial).
    [JSExport]
    public static string TouchTutorial() => WebWorldHud.Current?.TouchTutorial() ?? string.Empty;

    // The free auto-hunt: its state (with full, its options and choices), its log, the loot catalog classified, its options
    // set from the panel, and its profile code copied out or imported (GameplayHud.TouchHunt).
    [JSExport]
    public static string TouchHunt(bool full) => WebWorldHud.Current?.TouchHunt(full) ?? string.Empty;

    [JSExport]
    public static string TouchHuntLog() => WebWorldHud.Current?.TouchHuntLog() ?? string.Empty;

    [JSExport]
    public static string ClassifyHuntItems(string catalog) => WebWorldHud.Current?.ClassifyHuntItems(catalog) ?? string.Empty;

    [JSExport]
    public static bool SetFreeHuntSettings(string json) => WebWorldHud.Current?.SetFreeHuntSettings(json) ?? false;

    [JSExport]
    public static string FreeHuntProfileCode() => WebWorldHud.Current?.FreeHuntProfileCode() ?? string.Empty;

    [JSExport]
    public static bool ImportFreeHuntProfile(string code) => WebWorldHud.Current?.ImportFreeHuntProfile(code) ?? false;

    // The NPC plate: an NPC's dialog, a quest's dialog or a yes/no box (GameplayHud.TouchNpc).
    [JSExport]
    public static string TouchNpc() => WebWorldHud.Current?.TouchNpc() ?? string.Empty;

    // The combat feed's newest lines and the kills since entering the world (GameplayHud.TouchFeed).
    [JSExport]
    public static string TouchFeed() => WebWorldHud.Current?.TouchFeed() ?? string.Empty;

    // The party: the reached player's buttons, the other members and the open request (GameplayHud.TouchParty).
    [JSExport]
    public static string TouchParty() => WebWorldHud.Current?.TouchParty() ?? string.Empty;

    // The death plate: the restart window's choices, their cost and the seconds left (GameplayHud.TouchRestart).
    [JSExport]
    public static string TouchRestart() => WebWorldHud.Current?.TouchRestart() ?? string.Empty;

    // Eventos & Recompensas: the reward tracks and their entries, and the menu's requests to open it (GameplayHud.TouchRewards).
    [JSExport]
    public static string TouchRewards() => WebWorldHud.Current?.TouchRewards() ?? string.Empty;

    // A skill button's, belt potion's, item's or field map's picture named in TouchState, TouchBag or TouchMap, decoded
    // (GameplayHud.TouchPicture).
    [JSExport]
    public static byte[] TouchPicture(string path) => WebWorldHud.Current?.TouchPicture(path) ?? [];
}
