using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// A news post's lines, measured once, in pixels from the post's top: the kind's badge, the tools mark of a new system and
// the title in bold, wrapped under itself; the day and author; then the summary and the body in bold, a gap between
// paragraphs, the "- " lines as bullets whose wrapped lines hang under their text. **bold** spans are drawn brighter.
// Bold is drawn twice, a pixel apart, so it is one pixel wider than its measure.
internal sealed partial class HudNews
{
    private const string BoldMark = "**";
    private const string BulletMark = "- ";
    private const int BadgeHeight = 17;
    private const int BadgeGap = 7;
    private const int BulletIndent = 15;
    private const int TitleTop = 4;
    private const int TitleLine = 20;
    private const int StampLine = 18;
    private const int BodyLine = 19;
    private const int ParagraphGap = 11;
    private const int PostBottom = 10;
    private const int PostGap = 14;
    private const float TitleScale = HudPainter.TextScale * 1.15f;
    private const float StampScale = HudPainter.TextScale * 0.85f;
    private const float BodyScale = HudPainter.TextScale;
    private const float BadgeScale = HudPainter.TextScale * 0.85f;

    private static NewsLayout LayoutCard(HudPainter painter, NewsCard card, int width)
    {
        var rows = new List<NewsRow>();
        var y = TitleTop;
        var titleX = BadgeWidth(painter, card.Kind) + BadgeGap + (card.NewSystem ? ToolsIcon.Length + BadgeGap : 0);
        foreach (var line in Wrap(painter, BoldMark + card.Title + BoldMark, width - titleX, TitleScale))
        {
            rows.Add(new NewsRow(NewsRowKind.Title, Spans(painter, line, titleX, TitleScale), y, TitleScale));
            y += TitleLine;
            titleX = 0;
        }

        rows.Add(new NewsRow(NewsRowKind.Stamp, [new NewsSpan(card.Stamp, 0, false)], y, StampScale));
        y += StampLine + 4;
        var gap = false;
        foreach (var paragraph in card.Lines)
        {
            if (paragraph.Trim().Length == 0)
            {
                gap = true;
                continue;
            }

            var bullet = paragraph.StartsWith(BulletMark, StringComparison.Ordinal);
            if (gap || (!bullet && rows[^1].Kind == NewsRowKind.Bullet))
            {
                y += ParagraphGap;
            }

            gap = !bullet;
            var x = bullet ? BulletIndent : 0;
            var lines = Wrap(painter, bullet ? paragraph[BulletMark.Length..] : paragraph, width - x, BodyScale);
            for (var line = 0; line < lines.Count; line++)
            {
                var kind = bullet && line == 0 ? NewsRowKind.Bullet : NewsRowKind.Body;
                rows.Add(new NewsRow(kind, Spans(painter, lines[line], x, BodyScale), y, BodyScale));
                y += BodyLine;
            }
        }

        return new NewsLayout(rows.ToArray(), y + PostBottom, y + PostBottom + PostGap);
    }

    // The new-system mark beside the title, a hammer crossed with a wrench as the development channel shows it:
    // H the hammer's head, h its shine, W its handle, M the wrench, m its shade.
    private static readonly string[] ToolsIcon =
    [
        "..mMMm....HHHH..",
        ".mM..Mm..HHHHHH.",
        ".M....M.hHHHHHH.",
        ".Mm..mMhHHHHHH..",
        "..mMMMMMHHHHH...",
        "......MMMHH.....",
        "......WMMM......",
        ".....WW.MMM.....",
        "....WW...MMM....",
        "...WW.....MMM...",
        "..WW.......MMm..",
        ".WW.........MMm.",
        "WW...........mM.",
        "W..............."
    ];

    private static readonly Color ToolsHammer = new(150, 157, 167);
    private static readonly Color ToolsShine = new(214, 220, 228);
    private static readonly Color ToolsWood = new(176, 112, 58);
    private static readonly Color ToolsWrench = new(188, 196, 206);
    private static readonly Color ToolsShade = new(120, 128, 140);

    private static void DrawToolsIcon(HudPainter painter, int x, int y)
    {
        for (var row = 0; row < ToolsIcon.Length; row++)
        {
            var pixels = ToolsIcon[row];
            for (var column = 0; column < pixels.Length; column++)
            {
                var color = pixels[column] switch
                {
                    'H' => ToolsHammer,
                    'h' => ToolsShine,
                    'W' => ToolsWood,
                    'M' => ToolsWrench,
                    'm' => ToolsShade,
                    _ => Color.Transparent
                };
                if (color.A > 0)
                {
                    painter.Fill(new Rectangle(x + column, y + row, 1, 1), color);
                }
            }
        }
    }

    private static int BadgeWidth(HudPainter painter, NewsKind kind) => painter.MeasureAt(KindLabel(kind), BadgeScale) + 16;

    // Wrapped lines that each open and close their own bold marks, so a bold run broken across lines stays bold.
    internal static List<string> WrapBold(string text, float width, Func<string, float> measure)
    {
        var lines = HudTextLayout.Wrap(text, width, candidate => measure(candidate.Replace(BoldMark, "", StringComparison.Ordinal)));
        var bold = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = bold ? BoldMark + lines[index] : lines[index];
            bold = (Count(line) & 1) == 1;
            lines[index] = bold ? line + BoldMark : line;
        }

        return lines;
    }

    // Body text is drawn bold everywhere, so every run measures one pixel wider.
    private static List<string> Wrap(HudPainter painter, string text, int width, float scale) =>
        WrapBold(text, width, value => painter.MeasureAt(value, scale) + 1);

    private static NewsSpan[] Spans(HudPainter painter, string line, int x, float scale)
    {
        var spans = new List<NewsSpan>();
        var parts = line.Split(BoldMark);
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0)
            {
                spans.Add(new NewsSpan(parts[index], x, (index & 1) == 1));
                x += painter.MeasureAt(parts[index], scale) + 1;
            }
        }

        return spans.ToArray();
    }

    private static int Count(string line)
    {
        var count = 0;
        for (var at = line.IndexOf(BoldMark, StringComparison.Ordinal); at >= 0; at = line.IndexOf(BoldMark, at + BoldMark.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
