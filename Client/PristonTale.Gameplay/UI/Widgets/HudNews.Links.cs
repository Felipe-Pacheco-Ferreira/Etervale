using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The question a community button asks before it leaves the game: what opens and its address, Cancelar (Esc) and
// ABRIR (Enter), which opens the address in a new browser tab. While it is open it takes every click of the window.
internal sealed partial class HudNews
{
    private const int NoLink = -1;
    private static readonly Rectangle LinkBox = new(Box.Center.X - 170, Box.Y + 160, 340, 200);
    private static readonly Rectangle LinkCancel = new(LinkBox.X + 82, LinkBox.Bottom - 54, 120, 38);
    private static readonly Rectangle LinkOpen = new(LinkBox.X + 210, LinkBox.Bottom - 54, 116, 38);
    private static readonly string[] LinkQuestions = ["News.Link.Discord", "News.Link.Instagram", "News.Link.X"];

    private int _askedLink = NoLink;

    public bool LinkPrompt => _open && _askedLink != NoLink;

    // Enter: opens the asked address.
    public void ConfirmLink()
    {
        if (LinkPrompt && _social[_askedLink] is { } address)
        {
            DesktopCommunityLinks.Open(address);
        }

        _askedLink = NoLink;
    }

    private bool ClickSocial(Point pointer)
    {
        for (var index = 0; index < _social.Length; index++)
        {
            if (_social[index] is not null && SocialBounds(index).Contains(pointer))
            {
                _askedLink = index;
                return true;
            }
        }

        return false;
    }

    private void ClickLinkPrompt(Point pointer)
    {
        if (LinkOpen.Contains(pointer))
        {
            ConfirmLink();
        }
        else if (LinkCancel.Contains(pointer))
        {
            _askedLink = NoLink;
        }
    }

    // The address as players read it: no scheme and no closing slash.
    internal static string ShownAddress(Uri address)
    {
        var text = address.AbsoluteUri;
        var start = text.IndexOf("://", StringComparison.Ordinal);
        return (start >= 0 ? text[(start + 3)..] : text).TrimEnd('/');
    }

    private void DrawLinkPrompt(HudPainter painter)
    {
        if (!LinkPrompt || _social[_askedLink] is not { } address)
        {
            return;
        }

        painter.FramedBox(LinkBox, WindowBorder, WindowTop, WindowBottom, 6);
        painter.Disc(LinkBox.X + 26, LinkBox.Y + 24, 14, new Color(120, 92, 48));
        painter.Disc(LinkBox.X + 26, LinkBox.Y + 24, 12, new Color(206, 162, 76));
        BoldText(painter, "?", LinkBox.X + 23, LinkBox.Y + 16, new Color(44, 32, 16), HudPainter.TextScale * 1.1f);
        BoldText(painter, ClientText.Get("News.Link.Title"), LinkBox.X + 50, LinkBox.Y + 15, TitleCream, HudPainter.TextScale * 1.2f);
        painter.Fill(new Rectangle(LinkBox.X + 1, LinkBox.Y + 48, LinkBox.Width - 2, 1), Rule);
        var y = LinkBox.Y + 62;
        var question = ClientText.Get(LinkQuestions[_askedLink]);
        foreach (var line in HudTextLayout.Wrap(question, LinkBox.Width - 36, value => painter.MeasureAt(value, BodyScale) + 1))
        {
            BoldText(painter, line, LinkBox.X + 18, y, Color.White, BodyScale);
            y += BodyLine;
        }

        BoldText(painter, ShownAddress(address), LinkBox.X + 18, y + 10, new Color(150, 160, 255), BodyScale);
        DrawPromptButton(painter, LinkCancel, ClientText.Get("News.Link.Cancel"), "Esc", false);
        DrawPromptButton(painter, LinkOpen, ClientText.Get("News.Link.Open"), "Enter", true);
    }

    // A button of the question: its label in bold and its key in a small dark cap; ABRIR green.
    private static void DrawPromptButton(HudPainter painter, Rectangle bounds, string label, string key, bool primary)
    {
        painter.FramedBox(bounds, primary ? new Color(96, 190, 104) : new Color(150, 118, 66),
            primary ? new Color(52, 140, 62) : new Color(64, 49, 32), primary ? new Color(30, 96, 40) : new Color(40, 30, 20), 5);
        var keyScale = HudPainter.TextScale * 0.75f;
        var labelWidth = painter.MeasureAt(label, BodyScale) + 1;
        var keyWidth = painter.MeasureAt(key, keyScale) + 10;
        var x = bounds.Center.X - (labelWidth + 8 + keyWidth) / 2;
        BoldText(painter, label, x, bounds.Center.Y - 7, Color.White, BodyScale);
        var cap = new Rectangle(x + labelWidth + 8, bounds.Center.Y - 7, keyWidth, 14);
        painter.RoundedFill(cap, primary ? new Color(24, 70, 30) : new Color(26, 20, 14), 3);
        painter.Text(key, cap.X + 5, cap.Y + 2, new Color(220, 220, 210), keyScale);
    }

    private static void BoldText(HudPainter painter, string text, int x, int y, Color color, float scale)
    {
        painter.Text(text, x, y, color, scale);
        painter.Text(text, x + 1, y, color, scale);
    }
}
