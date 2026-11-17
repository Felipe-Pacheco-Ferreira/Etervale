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

    public static HudSystemEntry? At(Point design) =>
        Bounds.Contains(design) ? Rows[(design.Y - Bounds.Top) / Height].Entry : null;

    public static void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        var portuguese = ClientText.Language == "pt-BR";
        for (var index = 0; index < Rows.Length; index++)
        {
            var row = Row(index);
            row.Offset(0, layout.BottomOffset);
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
}
