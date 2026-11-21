using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Browser;

// The page's Settings panel (wwwroot/settings.js) edits the client settings as client-settings.json's document and keeps
// the saved one in the page's storage, since the browser has no file beside the executable. LoadSettings hands it in
// before Start; SaveSettings applies a change to the running game at once.
public static partial class BrowserHost
{
    private static PresentationSettings? _presentation;

    // The settings the game starts with: the saved document, or the defaults at the Low preset. The browser starts with
    // the classic light, also for a document saved before the choice existed.
    internal static ClientSettings StartSettings { get; private set; } =
        new ClientSettings().WithQuality(GraphicsQuality.Low) with { ClassicLighting = true };

    private const string ClassicLightingKey = "classicLighting";

    internal static void Attach(PresentationSettings presentation) => _presentation = presentation;

    private static ClientSettings Current => _presentation?.Requested ?? StartSettings;

    private static ClientSettings Parse(string json) => ClientSettingsStore.Parse(json, ConsoleClientLog.Instance) ?? Current;

    [JSExport]
    public static void LoadSettings(string json)
    {
        if (ClientSettingsStore.Parse(json, ConsoleClientLog.Instance) is { } saved)
        {
            var chosen = JsonNode.Parse(json) is JsonObject document && document.ContainsKey(ClassicLightingKey);
            StartSettings = chosen ? saved : saved with { ClassicLighting = true };
        }
    }

    [JSExport]
    public static string Settings() => ClientSettingsStore.Serialize(Current);

    // The page resolves Auto by the device's memory and calls this before the game decodes a texture.
    [JSExport]
    public static void SetTextureQuality(string quality) =>
        BrowserSceneTextures.Divisor = TextureDownscale.Divisor(Enum.TryParse<TextureQuality>(quality, out var parsed) ? parsed : TextureQuality.Original);

    // The local player's life for the page's death and low-life effects: -1 outside the world, 0 dead, else the fraction.
    [JSExport]
    public static double PlayerLife() => WebWorldHud.Current?.PlayerLife ?? -1;

    // F12 hides the game's HUD; the page hides its own HUD with it.
    [JSExport]
    public static bool HudHidden() => WebWorldHud.Current?.HudHidden ?? false;

    // The server translates the chat: the page shows its "Traduzir chat para" row.
    [JSExport]
    public static bool ChatTranslationAvailable() => WebWorldHud.Current?.ChatTranslationAvailable ?? false;

    // The panel's PvP button, as K in game: false outside the game, where there is no mode to switch.
    [JSExport]
    public static bool SwitchPvp()
    {
        if (WebWorldHud.Current is not { } hud)
        {
            return false;
        }

        hud.RequestPvpToggle();
        return true;
    }

    [JSExport]
    public static string SettingsWithQuality(string json, string quality) =>
        ClientSettingsStore.Serialize(Enum.TryParse<GraphicsQuality>(quality, out var preset) && preset != GraphicsQuality.Custom
            ? Parse(json).WithQuality(preset)
            : Parse(json));

    // Returns the document as applied (validated); the page stores that one.
    [JSExport]
    public static string SaveSettings(string json)
    {
        var current = Current;
        var settings = Parse(json) with { Language = current.Language, NewsEndpoint = current.NewsEndpoint };
        if (_presentation is { } presentation)
        {
            presentation.Requested = settings;
            presentation.ApplyRendering(settings);
            _game?.ResetFrameProfiling();
        }
        else
        {
            StartSettings = settings;
        }

        return ClientSettingsStore.Serialize(settings);
    }

    // Grouped display rows carry the original binding slot so storage and edits are independent of display order.
    [JSExport]
    public static string Shortcuts(string json, string language)
    {
        var settings = Parse(json);
        var shortcuts = HudSettingsPanel.PageShortcutRows;
        var rows = new ShortcutRow[shortcuts.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var (slot, group) = shortcuts[index];
            rows[index] = new ShortcutRow(slot, group, HudSettingsPanel.ShortcutName(slot, language),
                HudSettingsPanel.PageShortcutLabel(settings, slot), HudSettingsPanel.PageShortcutDefaultLabel(slot), Available(slot));
        }

        return JsonSerializer.Serialize(rows, ShortcutJson);
    }

    // keyCode: the browser's key code, which for the keys a shortcut takes equals the game's Keys value. An empty string
    // when the key cannot take the shortcut.
    [JSExport]
    public static string BindShortcut(string json, int slot, int keyCode, bool shift, bool alt)
    {
        var rebound = Available(slot) ? HudSettingsPanel.Rebind(Parse(json), slot, (Keys)keyCode, shift, alt) : null;
        return rebound is null ? string.Empty : ClientSettingsStore.Serialize(rebound);
    }

    [JSExport]
    public static string ResetShortcut(string json, int slot)
    {
        var restored = Available(slot) ? HudSettingsPanel.ResetShortcut(Parse(json), slot) : null;
        return restored is null ? string.Empty : ClientSettingsStore.Serialize(restored);
    }

    [JSExport]
    public static string RestoreShortcuts(string json) => ClientSettingsStore.Serialize(Parse(json) with { ShortcutKeys = [] });

    [JSExport]
    public static string ResetInterface(string json) => ClientSettingsStore.Serialize(Parse(json).WithInterfaceReset());

    private static bool Available(int slot) => HudSettingsPanel.OnPage(slot);

    private static readonly JsonSerializerOptions ShortcutJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly record struct ShortcutRow(int Slot, string Group, string Name, string Key, string DefaultKey, bool Available);
}
