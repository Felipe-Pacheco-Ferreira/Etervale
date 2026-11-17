using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The panel's look: a framed window with icon tabs, two titled cards of rows and a footer with the hints and buttons.
// A row label "Name: value" is drawn as a switch when the value is on or off, and as a name with a drop-down box
// otherwise; clicking anywhere on the row still cycles the value (Click).
internal sealed partial class HudSettingsPanel
{
    private const int ValueBoxWidth = 116;
    private const int KeyBoxWidth = 92;
    private const int CardTop = 121;
    private const int CardHeight = 353;
    private static readonly Color Gold = new(232, 199, 125);
    private static readonly Color TitleGold = new(238, 205, 140);
    private static readonly Color DimGold = new(110, 85, 45);
    private static readonly Color Edge = new(70, 55, 33);
    private static readonly Color Window = new(20, 17, 13);
    private static readonly Color TitleBar = new(36, 29, 19);
    private static readonly Color CardHeader = new(40, 33, 23);
    private static readonly Color CardBody = new(27, 23, 17);
    private static readonly Color Field = new(15, 12, 9);
    private static readonly Color FieldHover = new(36, 29, 20);
    private static readonly Color TabFill = new(26, 19, 11);
    private static readonly Color TabActive = new(52, 36, 16);
    private static readonly Color SwitchOn = new(58, 140, 70);
    private static readonly Color SwitchOnEdge = new(112, 196, 116);
    private static readonly Color SwitchOff = new(38, 34, 29);
    private static readonly Color SwitchOffEdge = new(88, 78, 64);
    private static readonly Color KnobOn = new(244, 240, 222);
    private static readonly Color KnobOff = new(146, 136, 116);
    private static readonly Color SaveTop = new(56, 136, 70);
    private static readonly Color SaveBottom = new(36, 98, 48);
    private static readonly Color SaveEdge = new(196, 160, 80);
    private static readonly Color CancelFill = new(46, 35, 22);
    private static readonly Color CancelTop = new(74, 58, 36);
    private static readonly Color TabActiveTop = new(104, 76, 38);
    private static readonly Color FieldTop = new(28, 23, 17);
    private static readonly Color CardBodyTop = new(34, 28, 20);
    private static readonly Color WindowBottom = new(12, 10, 8);
    private static readonly Color TitleBarTop = new(52, 42, 27);
    private static readonly float TitleScale = HudPainter.TextScale * 1.3f;
    private static readonly float HeadingScale = HudPainter.TextScale * 1.1f;

    public void Draw(HudPainter painter, Point pointer)
    {
        RefreshLabels();
        var portuguese = ClientText.Language == "pt-BR";
        DrawWindow(painter, pointer);
        DrawTabs(painter, pointer, portuguese ? PortugueseTabs : EnglishTabs);

        var headings = _tab switch
        {
            0 => portuguese ? ("Texturas", "Interface e nomes") : ("Textures", "Interface and names"),
            1 => portuguese ? ("Música de fundo", "Efeitos sonoros") : ("Background music", "Sound effects"),
            2 => portuguese ? ("Janelas", "Habilidades") : ("Windows", "Skills"),
            _ => portuguese ? ("Idioma", "Interface") : ("Language", "Interface")
        };
        if (_tab == 1)
        {
            DrawCard(painter, 109, headings.Item1, CardTop, 87);
            DrawCard(painter, 401, headings.Item2, CardTop, 87);
            DrawCard(painter, 109, portuguese ? "Chat de voz" : "Voice chat", 221, 186);
            DrawCard(painter, 401, portuguese ? "Microfone" : "Microphone", 221, 186);
        }
        else
        {
            DrawCard(painter, 109, headings.Item1);
            DrawCard(painter, 401, headings.Item2);
        }

        var current = _draft ?? _settings.Requested;
        if (_tab == 2)
        {
            for (var index = 0; index < Math.Min(VisibleRows, ShortcutRows.Length - _firstRow); index++)
            {
                var shortcutIndex = ShortcutRows[index + _firstRow];
                if (shortcutIndex < 0)
                {
                    var section = shortcutIndex switch
                    {
                        -9 => portuguese ? "Interface" : "Interface",
                        -10 => portuguese ? "Personagem" : "Character",
                        -11 => portuguese ? "Voz" : "Voice",
                        -12 => portuguese ? "Poções" : "Potions",
                        -7 => portuguese ? "Câmera" : "Camera",
                        _ => string.Empty
                    };
                    if (section.Length > 0)
                    {
                        var heading = Row(index);
                        painter.RoundedGradient(heading, CardHeader, CardBody, 2);
                        painter.Diamond(heading.X + 8, heading.Center.Y, 3, Gold);
                        painter.Text(section, heading.X + 17, heading.Y + 5, TitleGold);
                    }

                    continue;
                }
                var shortcut = Shortcuts[shortcutIndex];
                var capturing = shortcutIndex == _binding;
                var key = capturing ? (portuguese ? "Pressione uma tecla" : "Press a key") : BindingLabel(BindingOf(current, shortcutIndex));
                var row = Row(index);
                var keyBounds = new Rectangle(row.Right - KeyBoxWidth, row.Top, KeyBoxWidth, row.Height);
                painter.Text(painter.Fit(portuguese ? shortcut.Portuguese : shortcut.English, row.Width - KeyBoxWidth - 10),
                    row.X + 4, row.Y + 5, Color.Wheat);
                DrawField(painter, keyBounds, key, pointer, capturing, centered: true);
            }

            DrawButton(painter, RestoreKeys, portuguese ? "Restaurar padrão" : "Restore defaults", pointer);
        }
        else
        {
            var rows = Tabs[_tab];
            for (var index = 0; index < Math.Min(VisibleRows, rows.Length - _firstRow); index++)
            {
                var option = rows[index + _firstRow];
                var row = Row(index);
                if (option == -1)
                {
                    continue;
                }

                if (option < -1)
                {
                    var title = option switch
                    {
                        -2 => portuguese ? "Qualidade gráfica" : "Graphics quality",
                        -3 => portuguese ? "Gráficos aprimorados" : "Enhanced graphics",
                        -4 => portuguese ? "Exibição e resolução" : "Display and resolution",
                        -5 => portuguese ? "Jogo" : "Game",
                        -6 => portuguese ? "Filtro de loot" : "Loot filter",
                        -7 => portuguese ? "Câmera" : "Camera",
                        _ => portuguese ? "Notificações do chat" : "Chat notifications"
                    };
                    painter.RoundedGradient(row, CardHeader, CardBody, 2);
                    painter.Diamond(row.X + 8, row.Center.Y, 3, Gold);
                    painter.Text(title, row.X + 17, row.Y + 5, TitleGold);
                }
                else if (option is 100 or 101 or 102)
                {
                    DrawVolume(painter, row, option switch
                    {
                        100 => current.MusicVolumePercent,
                        101 => current.SoundVolumePercent,
                        _ => current.VoiceVolumePercent
                    });
                }
                else if (option == 109 && _binding == VoiceShortcut)
                {
                    DrawField(painter, row, portuguese ? "Pressione uma tecla" : "Press a key", pointer, true, centered: true);
                }
                else if (option == 196)
                {
                    DrawField(painter, row, EditingLootNames ? _lootNameDraft + "|" : _labels[option], pointer, EditingLootNames, centered: false);
                }
                else if (option is not (110 or 111 or 114))
                {
                    DrawOption(painter, row, _labels[option], pointer);
                }
            }
        }

        var totalRows = _tab == 2 ? ShortcutRows.Length : Tabs[_tab].Length;
        if (totalRows > VisibleRows)
        {
            var track = new Rectangle(692, CardTop, 4, CardHeight - 1);
            var thumb = Math.Max(20, track.Height * VisibleRows / totalRows);
            var max = (totalRows - VisibleRows + 1) / 2 * 2;
            painter.RoundedFill(track, Field);
            painter.RoundedFill(new Rectangle(track.X, track.Y + (track.Height - thumb) * _firstRow / max, track.Width, thumb), Gold);
        }

        var hint = EditingLootNames
            ? (portuguese ? "Separe os nomes por vírgula. Enter confirma, Esc cancela." : "Separate names with commas. Enter confirms, Esc cancels.")
            : _tab == 2
            ? (portuguese ? "Clique para alterar. Shift + clique restaura o atalho." : "Click to rebind. Shift + click restores the shortcut.")
            : Tabs[_tab].Length > VisibleRows
                ? (portuguese ? "Role para ver mais opções." : "Scroll for more options.")
                : ClientText.Get("Settings.Hint");
        painter.Text(painter.Fit(hint, _tab == 2 ? 370 : 580), 111, 488, HudTextColors.Hint);
        painter.Text(painter.Fit(_settings.EffectiveDisplay, 580), 111, 507, HudTextColors.Hint);
        DrawFooter(painter, pointer, portuguese);
    }

    private static void DrawWindow(HudPainter painter, Point pointer)
    {
        var box = Box;
        painter.RoundedFill(box, Gold, 3);
        box.Inflate(-1, -1);
        painter.RoundedFill(box, Field, 3);
        box.Inflate(-1, -1);
        painter.RoundedFill(box, Edge, 2);
        box.Inflate(-1, -1);
        painter.RoundedGradient(box, Window, WindowBottom, 2);

        var title = new Rectangle(box.X, box.Y, box.Width, 39);
        painter.RoundedGradient(title, TitleBarTop, TitleBar, 2);
        painter.Fill(new Rectangle(title.X, title.Bottom, title.Width, 1), DimGold);
        painter.Fill(new Rectangle(title.X + 12, title.Bottom, 180, 1), Gold);
        painter.Diamond(112, 35, 5, Gold);
        painter.Diamond(112, 35, 2, TitleBar);
        painter.Text(ClientText.Get("Settings.Title"), 125, 27, TitleGold, TitleScale);

        var close = CloseButton;
        var hover = close.Contains(pointer);
        painter.Disc(close.Center.X, close.Center.Y, 12, hover ? Gold : DimGold);
        painter.Disc(close.Center.X, close.Center.Y, 11, hover ? FieldHover : Field);
        painter.TextCentered("X", close.Left + 1, close.Right, close.Top + 6, hover ? Color.White : Color.Wheat);
    }

    private void DrawTabs(HudPainter painter, Point pointer, string[] names)
    {
        for (var tab = 0; tab < names.Length; tab++)
        {
            var bounds = Tab(tab);
            var active = tab == _tab;
            var hover = bounds.Contains(pointer);
            if (active)
            {
                painter.RoundedFill(new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), Gold * 0.3f, 3);
                painter.FramedBox(bounds, Gold, Gold, 2);
                bounds.Inflate(-2, -2);
                painter.RoundedGradient(bounds, TabActiveTop, TabActive, 2);
                bounds.Inflate(2, 2);
            }
            else
            {
                painter.FramedBox(bounds, hover ? DimGold : Edge, hover ? TabActiveTop : CardHeader, TabFill, 2);
            }

            var icon = new Rectangle(bounds.X + 8, bounds.Y + 7, 29, 29);
            painter.Disc(icon.Center.X, icon.Center.Y, 14, active ? Gold : DimGold);
            painter.Disc(icon.Center.X, icon.Center.Y, 13, active ? TabActive : Field);
            DrawTabIcon(painter, tab, icon, active ? TitleGold : Gold);
            painter.Text(names[tab], bounds.X + 46, bounds.Y + 15, active ? Color.White : Color.Wheat, HeadingScale);
            if (active)
            {
                painter.Fill(new Rectangle(bounds.X + 5, bounds.Bottom - 3, bounds.Width - 10, 2), Gold);
                painter.Diamond(bounds.Center.X, bounds.Bottom + 1, 4, Gold);
            }
        }
    }

    // Small pictograms built from fills: a monitor, a speaker, a keyboard and a cog.
    private static void DrawTabIcon(HudPainter painter, int tab, Rectangle icon, Color color)
    {
        var x = icon.X;
        var y = icon.Y;
        switch (tab)
        {
            case 0:
                painter.Fill(new Rectangle(x + 6, y + 7, 17, 12), color);
                painter.Fill(new Rectangle(x + 8, y + 9, 13, 8), Field);
                painter.Fill(new Rectangle(x + 13, y + 19, 3, 2), color);
                painter.Fill(new Rectangle(x + 10, y + 21, 9, 1), color);
                break;
            case 1:
                painter.Fill(new Rectangle(x + 6, y + 12, 4, 6), color);
                for (var column = 0; column < 5; column++)
                {
                    painter.Fill(new Rectangle(x + 10 + column, y + 12 - column, 1, 6 + 2 * column), color);
                }

                painter.Fill(new Rectangle(x + 18, y + 12, 1, 6), color);
                painter.Fill(new Rectangle(x + 21, y + 9, 1, 12), color);
                break;
            case 2:
                painter.Fill(new Rectangle(x + 4, y + 9, 21, 12), color);
                painter.Fill(new Rectangle(x + 5, y + 10, 19, 10), Field);
                for (var key = 0; key < 5; key++)
                {
                    painter.Fill(new Rectangle(x + 7 + key * 3, y + 12, 2, 2), color);
                    painter.Fill(new Rectangle(x + 7 + key * 3, y + 15, 2, 2), color);
                }

                painter.Fill(new Rectangle(x + 10, y + 18, 9, 1), color);
                break;
            default:
                var center = icon.Center;
                painter.Fill(new Rectangle(center.X - 1, center.Y - 9, 3, 19), color);
                painter.Fill(new Rectangle(center.X - 9, center.Y - 1, 19, 3), color);
                painter.Disc(center.X, center.Y, 6, color);
                painter.Disc(center.X, center.Y, 2, Field);
                break;
        }
    }

    private static void DrawCard(HudPainter painter, int x, string heading, int top = CardTop, int height = CardHeight)
    {
        var card = new Rectangle(x, top, 284, height);
        painter.FramedBox(card, Edge, CardBodyTop, CardBody, 2);
        painter.RoundedGradient(new Rectangle(x + 1, top + 1, card.Width - 2, 25), TabActiveTop, CardHeader, 2);
        painter.Fill(new Rectangle(x + 1, top + 26, card.Width - 2, 1), DimGold);
        painter.Fill(new Rectangle(x + 1, top + 30, 2, card.Height - 38), Gold);
        painter.Diamond(x + 13, top + 13, 4, Gold);
        painter.Text(heading, x + 24, top + 6, TitleGold, HeadingScale);
    }

    private static void DrawOption(HudPainter painter, Rectangle row, string label, Point pointer)
    {
        var separator = label.LastIndexOf(": ", StringComparison.Ordinal);
        if (separator < 0)
        {
            DrawButton(painter, row, label, pointer);
            return;
        }

        var name = label[..separator];
        var value = label[(separator + 2)..];
        var enabled = IsOn(value);
        if (enabled || IsOff(value))
        {
            var track = new Rectangle(row.X + 4, row.Y + 4, 30, 14);
            painter.FramedBox(track, enabled ? SwitchOnEdge : SwitchOffEdge, enabled ? SwitchOnEdge : SwitchOffEdge, enabled ? SwitchOn : SwitchOff, 4);
            painter.Disc(enabled ? track.Right - 7 : track.X + 7, track.Center.Y, 5, enabled ? KnobOn : KnobOff);
            painter.Text(painter.Fit(name, row.Width - 46), row.X + 42, row.Y + 5, row.Contains(pointer) ? Color.White : Color.Wheat);
            return;
        }

        var box = new Rectangle(row.Right - ValueBoxWidth, row.Top, ValueBoxWidth, row.Height);
        painter.Text(painter.Fit(name, row.Width - ValueBoxWidth - 10), row.X + 4, row.Y + 5, Color.Wheat);
        var hover = row.Contains(pointer);
        painter.FramedBox(box, hover ? Gold : DimGold, hover ? FieldHover : FieldTop, Field, 2);
        painter.Text(painter.Fit(value, box.Width - 26), box.X + 7, box.Y + 5, Color.White);
        painter.DownTriangle(box.Right - 10, box.Y + 13, 4, 5, Gold);
    }

    // The on and off words of the labels (Toggle and the resource texts), in either language and case.
    private static bool IsOn(string value) => value.StartsWith("Ligad", StringComparison.OrdinalIgnoreCase)
        || value.Equals("On", StringComparison.OrdinalIgnoreCase);

    private static bool IsOff(string value) => value.StartsWith("Desligad", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Off", StringComparison.OrdinalIgnoreCase);

    private static void DrawField(HudPainter painter, Rectangle bounds, string text, Point pointer, bool active, bool centered)
    {
        var hover = bounds.Contains(pointer);
        painter.FramedBox(bounds, active || hover ? Gold : DimGold, active ? TabActiveTop : hover ? FieldHover : FieldTop, active ? TabActive : Field, 2);
        var fitted = painter.Fit(text, bounds.Width - 12);
        if (centered)
        {
            painter.TextCentered(fitted, bounds.Left, bounds.Right, bounds.Y + 5, Color.Wheat);
        }
        else
        {
            painter.Text(fitted, bounds.X + 7, bounds.Y + 5, Color.Wheat);
        }
    }

    private static void DrawButton(HudPainter painter, Rectangle bounds, string text, Point pointer)
    {
        var hover = bounds.Contains(pointer);
        painter.FramedBox(bounds, hover ? Gold : DimGold, hover ? TabActiveTop : CancelTop, hover ? TabActive : CancelFill, 2);
        painter.TextCentered(painter.Fit(text, bounds.Width - 12), bounds.Left, bounds.Right, bounds.Y + (bounds.Height - 12) / 2,
            hover ? Color.White : Color.Wheat);
    }

    private static void DrawVolume(HudPainter painter, Rectangle bounds, int volume)
    {
        var track = VolumeTrack(bounds);
        var filled = track.Width * Math.Clamp(volume, 0, 100) / 100;
        painter.RoundedFill(new Rectangle(track.X, track.Y - 1, track.Width, track.Height + 2), Field);
        painter.Fill(new Rectangle(track.X, track.Y, filled, track.Height), Gold);
        painter.Disc(track.X + filled, track.Center.Y, 6, DimGold);
        painter.Disc(track.X + filled, track.Center.Y, 5, TitleGold);
        painter.Text($"{volume}%", track.Right + 13, bounds.Y + 5, Color.White);
    }

    private static void DrawFooter(HudPainter painter, Point pointer, bool portuguese)
    {
        var footer = new Rectangle(96, 529, 608, 53);
        painter.Fill(new Rectangle(footer.X, footer.Y - 1, footer.Width, 1), DimGold);
        painter.RoundedGradient(footer, CardBody, Field, 0);

        var cancel = CancelButton;
        var hover = cancel.Contains(pointer);
        painter.FramedBox(cancel, hover ? Gold : DimGold, hover ? TabActiveTop : CancelTop, hover ? TabActive : CancelFill, 3);
        painter.TextCentered(portuguese ? "Cancelar" : "Cancel", cancel.Left, cancel.Right, cancel.Top + 10, hover ? Color.White : Color.Wheat);

        var save = OkButton;
        hover = save.Contains(pointer);
        painter.FramedBox(save, hover ? TitleGold : SaveEdge, hover ? SwitchOnEdge : SaveTop, SaveBottom, 3);
        painter.TextCentered(portuguese ? "SALVAR" : "SAVE", save.Left, save.Right, save.Top + 9, HeadingScale);
    }
}
