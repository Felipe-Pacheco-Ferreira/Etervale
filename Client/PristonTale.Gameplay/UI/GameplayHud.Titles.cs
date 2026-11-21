using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Titles;

namespace PristonTale.Gameplay.UI;

// Titles (TitleRules): a plaque over the status window with the title shown over the name (in its rarity's colour, or
// "no title") between two arrows that step through the titles the player owns and none. The choice goes to the server
// (TitleChosen), whose answer (PresenceUpdate.Titles) moves the plaque.
public sealed partial class GameplayHud
{
    private const int TitlePlaqueX = 20;
    private const int TitlePlaqueWidth = 250;
    private const int TitlePlaqueHeight = 22;
    private const int TitleArrowWidth = 22;
    private static readonly Color PlaqueBorder = new(143, 106, 62);
    private static readonly Color PlaqueTop = new(52, 40, 24);
    private static readonly Color PlaqueBottom = new(26, 19, 11);

    // The local player's titles, null until the server sent them.
    public TitlesInfo Titles { get; private set; }

    // A title the player asks to show (its code, 0 for none).
    public event Action<int> TitleChosen;

    public void ObserveTitles(PresenceUpdate update)
    {
        if (update.Titles is { } titles)
        {
            Titles = titles;
        }
    }

    // The plaque sits on the status window's top edge, over its left columns.
    private static Rectangle TitlePlaque(Point origin) =>
        new(origin.X + TitlePlaqueX, Widgets.HudStatusPanel.Window(origin).Top - TitlePlaqueHeight, TitlePlaqueWidth, TitlePlaqueHeight);

    private void DrawTitlePlaque(Point origin, Point pointer)
    {
        if (Titles is not { } titles)
        {
            return;
        }

        var plaque = TitlePlaque(origin);
        _painter.FramedBox(plaque, PlaqueBorder, PlaqueTop, PlaqueBottom, 3);
        var textY = plaque.Top + (plaque.Height - 13) / 2;
        var (previous, next) = TitleArrows(plaque);
        _painter.TextCentered("<", previous.Left, previous.Right, textY, previous.Contains(pointer) ? Color.White : Color.Wheat);
        _painter.TextCentered(">", next.Left, next.Right, textY, next.Contains(pointer) ? Color.White : Color.Wheat);
        var shown = titles.Shown;
        _painter.TextCentered(TitleRules.IsTitle(shown) ? ClientText.TitleName(shown) : ClientText.Get("Title.None"), previous.Right, next.Left, textY,
            TitleRules.IsTitle(shown) ? HudNameColors.Title(TitleRules.Of((TitleCode)shown).Rarity) : Color.Gray);
    }

    private bool TitlePlaqueCaptures(HudLayout layout, Point pointer) =>
        _statusSlide.FullyOpen && Titles is not null && TitlePlaque(PanelOrigin(layout, _statusSlide)).Contains(pointer);

    // A click on an arrow asks for the owned title before or after the one shown, none between the last and the first.
    private bool ClickTitlePlaque(Point origin, Point pointer)
    {
        if (Titles is not { } titles)
        {
            return false;
        }

        var (previous, next) = TitleArrows(TitlePlaque(origin));
        var step = previous.Contains(pointer) ? -1 : next.Contains(pointer) ? 1 : 0;
        if (step == 0)
        {
            return false;
        }

        var code = (int)titles.Shown;
        do
        {
            code = (code + step + TitleRules.Count) % TitleRules.Count;
        }
        while (code != 0 && !titles.Owns(code));

        TitleChosen?.Invoke(code);
        return true;
    }

    private static (Rectangle Previous, Rectangle Next) TitleArrows(Rectangle plaque) =>
        (new Rectangle(plaque.Left, plaque.Top, TitleArrowWidth, plaque.Height), new Rectangle(plaque.Right - TitleArrowWidth, plaque.Top, TitleArrowWidth,
            plaque.Height));
}
