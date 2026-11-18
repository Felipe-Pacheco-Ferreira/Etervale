using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

public enum HudSystemEntry
{
    Settings,
    Help,
    Exit,
    Characters,
    Servers
}

public static class HudSystemMenu
{
    private const int Width = 112;
    private const int Height = 27;
    private const int X = 688;
    private const int Bottom = 556;
    private const int BrowserX = 698;
    private const int BrowserFirstY = 412;
    private const int BrowserRowHeight = 20;
    private const int BrowserRowStep = 31;
    private const int BrowserWidth = 98;

    private static readonly (HudSystemEntry Entry, string Portuguese, string English)[] Rows =
    [
        (HudSystemEntry.Servers, "Servidores", "Servers"),
        (HudSystemEntry.Characters, "Personagens", "Characters"),
        (HudSystemEntry.Settings, "Configurações", "Settings"),
        (HudSystemEntry.Help, "Ajuda", "Help"),
        (HudSystemEntry.Exit, "Sair", "Quit")
    ];

    public static Rectangle Bounds => new(X, Bottom - Height * Rows.Length, Width, Height * Rows.Length);

    private static Rectangle Row(int index) => new(X, Bounds.Top + index * Height, Width, Height);
    private static Rectangle BrowserRow(int index) => new(BrowserX, BrowserFirstY + index * BrowserRowStep, BrowserWidth, BrowserRowHeight);

    public static Rectangle Button(HudSystemEntry entry, bool browser)
    {
        for (var index = 0; index < Rows.Length; index++)
        {
            if (Rows[index].Entry == entry)
            {
                return browser ? BrowserRow(index) : Row(index);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(entry));
    }

    public static bool Captures(Point design, bool browser) => browser
        ? new Rectangle(BrowserX, BrowserFirstY, BrowserWidth, BrowserRowStep * (Rows.Length - 1) + BrowserRowHeight).Contains(design)
        : Bounds.Contains(design);

    public static HudSystemEntry? At(Point design, bool browser = false)
    {
        if (!Captures(design, browser))
        {
            return null;
        }

        if (!browser)
        {
            return Rows[(design.Y - Bounds.Top) / Height].Entry;
        }

        var index = (design.Y - BrowserFirstY) / BrowserRowStep;
        return BrowserRow(index).Contains(design) ? Rows[index].Entry : null;
    }

    public static void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        if (layout.Browser && painter.Texture(HudArt.SystemMenu) is not null)
        {
            DrawBrowser(painter, layout, pointer);
            return;
        }

        var portuguese = ClientText.Language == "pt-BR";
        for (var index = 0; index < Rows.Length; index++)
        {
            var row = layout.Browser ? BrowserRow(index) : Row(index);
            row.Offset(layout.BottomBarOrigin);
            var hovered = row.Contains(pointer);
            painter.ColorBox(row, 128, 101, 71, 255);
            row.Inflate(-1, -1);
            painter.ColorBox(row, hovered ? 97 : 61, hovered ? 65 : 44, hovered ? 30 : 28, 255);
            painter.ColorBox(new Rectangle(row.Left, row.Top, row.Width, 2), 157, 128, 88, 255);
            painter.ColorBox(new Rectangle(row.Left, row.Bottom - 3, row.Width, 3), 44, 37, 32, 255);
            painter.ColorBox(new Rectangle(row.Left + 2, row.Top + 4, 3, 3), 177, 152, 119, 255);
            painter.ColorBox(new Rectangle(row.Right - 5, row.Top + 4, 3, 3), 177, 152, 119, 255);
            var label = portuguese ? Rows[index].Portuguese : Rows[index].English;
            painter.TextCentered(label, row.Left + 6, row.Right - 6, row.Top + 5, hovered ? Color.Wheat : Color.Gold);
        }
    }

    private static void DrawBrowser(HudPainter painter, HudLayout layout, Point pointer)
    {
        // The native atlas contains Help and Quit. Reuse its wood and metal pieces for the three extra plaques.
        var backgroundSource = new Rectangle(0, 0, BrowserWidth, BrowserRowStep);
        var plankSource = new Rectangle(13, 3, 13, 16);
        for (var index = 0; index < 3; index++)
        {
            var row = BrowserRow(index);
            row.Offset(layout.BottomBarOrigin);
            var hovered = row.Contains(pointer);
            painter.Sprite(HudArt.SystemMenu, row.X, row.Y, backgroundSource);
            var plank = new Rectangle(row.X + 13, row.Y + 3, BrowserWidth - 26, 16);
            painter.Stretch(HudArt.SystemMenu, plank, plankSource);
            if (hovered)
            {
                painter.ColorBox(plank, 235, 164, 69, 45);
            }

            painter.TextCentered(Rows[index].English, row.Left, row.Right, row.Top + 1, hovered ? Color.Wheat : new Color(255, 213, 75));
        }

        var help = BrowserRow(3);
        help.Offset(layout.BottomBarOrigin);
        painter.Sprite(HudArt.SystemMenu, help.X, help.Y);
        var quit = BrowserRow(4);
        quit.Offset(layout.BottomBarOrigin);
        if (help.Contains(pointer))
        {
            painter.Sprite(HudArt.SystemHelp, help.X, help.Y);
        }
        else if (quit.Contains(pointer))
        {
            painter.Sprite(HudArt.SystemExit, quit.X, quit.Y);
        }
    }
}
