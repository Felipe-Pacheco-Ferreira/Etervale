using System.Runtime.InteropServices.JavaScript;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Browser;

// The page's handle on the game (wwwroot/main.js): it fetches the files the game reads, starts it, then calls Frame on
// every animation frame, since the browser owns the frame loop (the runtime's asynchronous run behaviour). The page also
// draws the login form over the canvas: SubmitLogin hands in what was typed and reportLogin tells the page each step.
public static partial class BrowserHost
{
    private static BrowserGame? _game;

    // When Start was called, in Stopwatch milliseconds, for the start's timing log (BrowserGame.LoadContent).
    internal static double StartedMs { get; private set; }

    // clientAddress: the absolute address of the folder the client is served from, ending in '/'; language: the page's
    // language, the only one whose own interface font (UiFonts) is fetched.
    [JSExport]
    public static Task PrepareAsync(string clientAddress, string language) =>
        BootstrapFiles.FetchAsync(new Uri(clientAddress, UriKind.Absolute), ClientSettings.NormalizeLanguage(language));

    // endpoint: the game server's wss:// address; language: the page's language (the client's texts follow it).
    [JSExport]
    public static void Start(string endpoint, string language, string origin)
    {
        if (_game is not null)
        {
            throw new InvalidOperationException("The game is already running.");
        }

        StartedMs = System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency;
        WebCharacterSelectOverlay.ProfileStageInput = false;
        DesktopCommunityLinks.BrowserOpen = ShowCommunityLink;
        WebLoginOverlay.Report = static (state, message) => ReportLogin(state.ToString().ToLowerInvariant(), message);
        WebCharacterSelectOverlay.Report = ReportCharacters;
        WebWorldLoading.Report = ReportWorldLoading;
        PristonRenderer.ExternalSceneRendererFactory = static () => new BrowserExternalSceneRenderer();
        _game = new BrowserGame(endpoint, language, origin);
        _game.Run();
    }

    // The files after the login scene (BootstrapFiles), fetched while the login is on screen; the page logs in once done.
    [JSExport]
    public static Task FetchRemainingAsync() => BootstrapFiles.FetchRemainingAsync();

    [JSExport]
    public static Task PrefetchCharacterAssetsAsync() => BootstrapFiles.PrefetchCharacterAssetsAsync();

    // The opening flight's moment (WebOpeningClock): the page reads it to record the flight and sets it to its recording's
    // moment when the live scene takes over.
    [JSExport]
    public static double OpeningSeconds() => WebOpeningClock.Seconds;

    [JSExport]
    public static void SetOpeningSeconds(double seconds) => WebOpeningClock.Request(seconds);

    [JSExport]
    public static void SubmitLogin(string account, string password, string endpoint) => WebLoginOverlay.Submit(account, password, endpoint);

    // The page's character screen: kind is a WebCharacterCommandKind name (Select, Enter, Create, ...), index the row,
    // tribe, class or face it names, text the new character's name.
    [JSExport]
    public static void CharacterCommand(string kind, int index, string text)
    {
        if (Enum.TryParse<WebCharacterCommandKind>(kind, out var command))
        {
            if (WebCharacterSelectOverlay.ProfileStageInput)
            {
                Console.WriteLine($"[StagePick] host command={command} index={index}");
            }
            WebCharacterSelectOverlay.Send(command, index, text);
        }
    }

    [JSExport]
    public static void PickOnStage(string screen, double x, double y, double clickSeconds)
    {
        if (WebCharacterSelectOverlay.ProfileStageInput && screen is "roster" or "class")
        {
            Console.WriteLine(FormattableString.Invariant($"[StagePick] host pick stage={screen} x={x:F5} y={y:F5} eventMs={clickSeconds * 1000:F3}"));
        }
        if (x is >= 0d and <= 1d && y is >= 0d and <= 1d && double.IsFinite(clickSeconds) && clickSeconds >= 0d)
        {
            if (screen is "roster" or "class")
            {
                var stage = screen == "roster" ? WebCharacterStage.Roster : WebCharacterStage.Class;
                WebCharacterSelectOverlay.PickOnStage(stage, new Vector2((float)x, (float)y), clickSeconds);
            }
        }
    }

    [JSImport("reportCharacters", "avalon-page")]
    private static partial void ReportCharacters(string state);

    [JSImport("reportLoginProgress", "avalon-page")]
    internal static partial void ReportLoginProgress(int done, int total);

    [JSImport("reportWorldLoading", "avalon-page")]
    private static partial void ReportWorldLoading(string phase, int percent, string mapName);

    [JSImport("openWorldSettings", "avalon-page")]
    private static partial void OpenWorldSettings();

    [JSImport("reportLogin", "avalon-page")]
    private static partial void ReportLogin(string state, string message);

    [JSImport("showCommunityLink", "avalon-page")]
    private static partial void ShowCommunityLink(string address);

    [JSImport("openNews", "avalon-page")]
    private static partial void OpenNews();

    [JSExport]
    public static void Frame() => _game?.Tick();

    [JSExport]
    public static void EnableWorldHud()
    {
        WebWorldHud.Enabled = true;
        WebWorldHud.OpenSettings = OpenWorldSettings;
        HudNews.PageOpen = OpenNews;
    }

    // News & Support drawn by the page (news.js): the window's state and the tickets the game keeps, its actions, its close.
    [JSExport]
    public static string NewsState() => WebWorldHud.Current?.News.PageState() ?? string.Empty;

    [JSExport]
    public static bool NewsAction(string action, string id, string subject, int category, string text) =>
        WebWorldHud.Current?.News.PageAction(action, id, subject, category, text) ?? false;

    [JSExport]
    public static void NewsClosed() => WebWorldHud.Current?.News.PageClosed();

    [JSExport]
    public static void SetWorldCanvasPointer(bool overCanvas) => WebWorldHud.HasCanvasPointer = overCanvas;

    [JSExport]
    public static string WorldActions() => WebWorldHud.Current?.WorldActions() ?? string.Empty;

    [JSExport]
    public static bool RunWorldAction(string action) => WebWorldHud.Current?.RunWorldAction(action) ?? false;

    [JSExport]
    public static bool WorldAutoHuntActive() => WebWorldHud.Current?.AnyHuntRunning ?? false;

    // The touch HUD's "Auto-caça · Ligado" chip: a hunt runs and, for the free hunt, its Mostrar o quadro option is on.
    [JSExport]
    public static bool WorldHuntStatusShown() => WebWorldHud.Current?.HuntStatusShown ?? false;

    // The players online the page's counter shows; -1 while unknown or hidden in Settings.
    [JSExport]
    public static int OnlinePlayers() => WebWorldHud.Current?.OnlinePlayers ?? -1;

    // The mark on the page's News & Support shortcut: news not seen and answered tickets not read.
    [JSExport]
    public static int NewsUnread() => WebWorldHud.Current?.NewsUnread ?? 0;
}
