using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

// Word wrap: words (and Chinese and Japanese characters, Pieces) join a line while it measures within the width; a single
// word wider than the width keeps a line of its own. Runs of spaces collapse.
public static class HudTextLayout
{
    // The text, or the longest prefix that fits the width with an ellipsis (a prefix's width grows with its length, so it
    // is bisected).
    public static string Fit(string value, float width, Func<string, float> measure)
    {
        if (measure(value) <= width)
        {
            return value;
        }

        var fitted = string.Empty;
        for (int low = 1, high = value.Length - 1; low <= high;)
        {
            var length = (low + high) / 2;
            var candidate = value[..length] + "...";
            if (measure(candidate) <= width)
            {
                fitted = candidate;
                low = length + 1;
            }
            else
            {
                high = length - 1;
            }
        }

        return fitted;
    }

    public static string[] Wrap(SpriteFont font, string text, float width, float scale) =>
        Wrap(text, width, candidate => font.MeasureString(candidate).X * scale).ToArray();

    public static List<string> Wrap(string text, float width, Func<string, float> measure)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var (piece, space) in Pieces(text))
        {
            var candidate = line.Length == 0 ? piece : space ? line + " " + piece : line + piece;
            if (line.Length > 0 && measure(candidate) > width)
            {
                lines.Add(line);
                line = piece;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    // The pieces a line is built of, each with whether a space comes before it: the words between spaces, and, as Chinese
    // and Japanese write no spaces, each of their characters on its own, with the closing marks that follow it (a line
    // never starts with one).
    public static List<(string Piece, bool Space)> Pieces(string text)
    {
        var pieces = new List<(string, bool)>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var space = pieces.Count > 0;
            var start = 0;
            for (var index = 0; index < word.Length; index++)
            {
                // An opening mark goes with the character after it (a line never ends with one); a closing mark after
                // other text stays with it.
                var opening = OpeningMarks.Contains(word[index]) && index + 1 < word.Length && IsCjk(word[index + 1]);
                if (!opening && (!IsCjk(word[index]) || index > start && ClosingMarks.Contains(word[index])))
                {
                    continue;
                }

                if (index > start)
                {
                    pieces.Add((word[start..index], space));
                    space = false;
                }

                var end = opening ? index + 2 : index + 1;
                while (end < word.Length && ClosingMarks.Contains(word[end]))
                {
                    end++;
                }

                pieces.Add((word[index..end], space));
                space = false;
                start = end;
                index = end - 1;
            }

            if (start < word.Length)
            {
                pieces.Add((word[start..], space));
            }
        }

        return pieces;
    }

    // A Chinese or Japanese character or mark: CJK radicals and punctuation, kana, the unified ideographs and the
    // full-width forms.
    public static bool IsCjk(char value) => value is >= '\u2E80' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' or >= '\uFF00' and <= '\uFFEF';

    // The marks a line may not start with (full-width and, after a Chinese or Japanese character, ASCII ones too) and those
    // it may not end with.
    private const string ClosingMarks = "。，、！？：；）」』】》〉”’．…・ー々～!),.:;?]%";
    private const string OpeningMarks = "（「『【《〈“‘([";
}
