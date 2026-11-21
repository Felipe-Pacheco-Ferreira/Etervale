using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Help;

// The tutorial's tips (ClientSettings.TutorialTips). While a character is at most MostLevel, the first tip of Tips it has
// not closed shows in a guide box at the top of the screen, under the notice strip. "Got it" closes it for that character
// (ClientSettings.TutorialTipsClosed) and the next one follows; "Turn off tips" switches the option off. A tip's text
// (ClientText "Tutorial.<id>.Title" and ".Text") takes the keys bound to its shortcuts as {0}, {1}, ...
public sealed class HudTutorialTips(Func<int, string> keyLabel)
{
    public const int MostLevel = 20;
    // The closed tips kept, the oldest dropped first: a few tips for each of many characters.
    public const int MostClosed = 512;

    public sealed record Tip(string Id, int[] Shortcuts);

    public static readonly Tip[] Tips =
    [
        new("Move", []),
        new("Attack", []),
        new("Inventory", [HudSettingsPanel.InventoryShortcut]),
        new("Character", [HudSettingsPanel.CharacterShortcut]),
        new("Skills", [HudSettingsPanel.SkillsShortcut, HudSettingsPanel.FirstSkillShortcut, HudSettingsPanel.FirstSkillShortcut + HudSkillBindings.ShortcutCount - 1]),
        new("Potions", [HudSettingsPanel.FirstBeltShortcut, HudSettingsPanel.FirstBeltShortcut + 1, HudSettingsPanel.FirstBeltShortcut + 2]),
        new("Chat", []),
        new("Settings", [HudSettingsPanel.SettingsShortcut])
    ];

    private const int BoxY = 74;
    private const int Columns = 22;
    private const int MinRows = 3;
    private const int Pad = 24;
    private const int LineStep = HudPainter.LineHeight;
    private const int FooterGap = 6;
    private const int TextLines = 4;

    private ClientSettings _settings;
    private string _character;
    private int _level;
    private int _tip = -1;
    private string _language;
    private string _title;
    private string[] _lines;
    private string _ok;
    private string _off;
    private Rectangle _bounds;
    private Rectangle _okButton;
    private Rectangle _offButton;

    public bool Showing => _tip >= 0;

    // The index in Tips of the tip a character sees now, or -1: tips on, a level from 1 to MostLevel, and one not closed.
    public static int Current(ClientSettings settings, string character, int level)
    {
        if (!settings.TutorialTips || string.IsNullOrEmpty(character) || level is < 1 or > MostLevel)
        {
            return -1;
        }

        for (var index = 0; index < Tips.Length; index++)
        {
            if (!IsClosed(settings.TutorialTipsClosed, character, Tips[index].Id))
            {
                return index;
            }
        }

        return -1;
    }

    public static ClientSettings Closed(ClientSettings settings, string character, string id)
    {
        if (IsClosed(settings.TutorialTipsClosed, character, id))
        {
            return settings;
        }

        var closed = settings.TutorialTipsClosed;
        var kept = closed.Length >= MostClosed ? closed[(closed.Length - MostClosed + 1)..] : closed;
        return settings with { TutorialTipsClosed = [.. kept, character + "/" + id] };
    }

    // Whether the list holds "character/id", compared in place so the frame's check allocates nothing.
    private static bool IsClosed(string[] closed, string character, string id)
    {
        foreach (var entry in closed)
        {
            if (entry.Length == character.Length + 1 + id.Length && entry[character.Length] == '/'
                && entry.StartsWith(character, StringComparison.Ordinal) && entry.EndsWith(id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Looks again only when the settings, the character or its level changed.
    public void Update(ClientSettings settings, string character, int level)
    {
        if (ReferenceEquals(settings, _settings) && ReferenceEquals(character, _character) && level == _level)
        {
            return;
        }

        _settings = settings;
        _character = character;
        _level = level;
        _tip = Current(settings, character, level);
        _lines = null;
    }

    public bool Captures(Point pointer, bool blocked) => Showing && !blocked && _bounds.Contains(pointer);

    // Null when the click was not on the box; else the settings to keep, the same ones for a click on the box itself.
    public ClientSettings Click(Point pointer, bool blocked)
    {
        if (!Captures(pointer, blocked))
        {
            return null;
        }

        if (_okButton.Contains(pointer))
        {
            return Closed(_settings, _character, Tips[_tip].Id);
        }

        return _offButton.Contains(pointer) ? _settings with { TutorialTips = false } : _settings;
    }

    public void Draw(HudPainter painter, HudLayout layout, Point pointer, bool blocked)
    {
        if (!Showing || blocked)
        {
            return;
        }

        var width = HudPainter.GuideBoxRight(0, Columns) + HudPainter.GuideCorner;
        var x = Math.Max(0, (layout.Width - width) / 2);
        if (_lines is null || _language != ClientText.Language || _bounds.X != x)
        {
            Prepare(painter, x, width);
        }

        var rows = Math.Max(MinRows, (int)Math.Ceiling((_okButton.Bottom + FooterGap - HudPainter.GuideBoxBottom(BoxY, 0)) / (double)HudPainter.GuideRowStep));
        painter.GuideBox(x, BoxY, Columns, rows);
        painter.Flush();
        var textX = x + Pad;
        var y = BoxY + Pad;
        painter.Text(_title, textX, y, HudTextColors.BoxTitle);
        for (var index = 0; index < _lines.Length; index++)
        {
            painter.Text(_lines[index], textX, y + (index + 1) * LineStep, HudTextColors.HelpText);
        }

        painter.Text(_ok, _okButton.X, _okButton.Y, _okButton.Contains(pointer) ? HudTextColors.Marked : Color.White);
        painter.Text(_off, _offButton.X, _offButton.Y, _offButton.Contains(pointer) ? HudTextColors.Marked : HudTextColors.Hint);
        _bounds = new Rectangle(x, BoxY, width, HudPainter.GuideBoxBottom(BoxY, rows) + HudPainter.GuideCorner - BoxY);
    }

    private void Prepare(HudPainter painter, int x, int width)
    {
        _language = ClientText.Language;
        var tip = Tips[_tip];
        var keys = new object[tip.Shortcuts.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            keys[index] = keyLabel(tip.Shortcuts[index]);
        }

        _title = ClientText.Get("Tutorial." + tip.Id + ".Title");
        var text = ClientText.Format("Tutorial." + tip.Id + ".Text", keys);
        var lines = HudTextLayout.Wrap(text, width - Pad * 2, value => painter.Measure(value));
        _lines = [.. lines.Take(TextLines)];
        _ok = ClientText.Get("Tutorial.Ok");
        _off = ClientText.Get("Tutorial.Off");
        var footerY = BoxY + Pad + (_lines.Length + 1) * LineStep + FooterGap;
        var right = x + width - Pad;
        var okWidth = painter.Measure(_ok);
        _okButton = new Rectangle(right - okWidth, footerY, okWidth, LineStep);
        _offButton = new Rectangle(x + Pad, footerY, painter.Measure(_off), LineStep);
        _bounds = new Rectangle(x, BoxY, width, 0);
    }
}
