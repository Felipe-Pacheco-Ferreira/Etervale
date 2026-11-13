using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using PristonTale.Contracts.Social;
using PristonTale.Mobile;

// Chat lines as the HUD shows them, on screen and in the chat window: each line in its channel's colour, the speaker's
// name (up to the first colon) brighter, as the PC's chat does.
internal static class VulkanChatText
{
    public static SpannableStringBuilder Format(IReadOnlyList<VulkanWorldSession.ChatEntry> lines, int first)
    {
        var text = new SpannableStringBuilder();
        for (var index = Math.Max(0, first); index < lines.Count; index++)
        {
            if (text.Length() > 0)
            {
                text.Append("\n");
            }

            var (line, kind) = lines[index];
            var start = text.Length();
            text.Append(line);
            text.SetSpan(new ForegroundColorSpan(Body(kind)), start, text.Length(), SpanTypes.ExclusiveExclusive);
            var colon = line.IndexOf(':');
            if (colon > 0 && colon < 40)
            {
                text.SetSpan(new ForegroundColorSpan(Name(kind)), start, start + colon + 1, SpanTypes.ExclusiveExclusive);
                text.SetSpan(new StyleSpan(TypefaceStyle.Bold), start, start + colon + 1, SpanTypes.ExclusiveExclusive);
            }
        }

        return text;
    }

    private static Color Body(ChatKind kind) => kind switch
    {
        ChatKind.Party => Color.Rgb(121, 224, 255),
        ChatKind.Whisper => Color.Rgb(244, 177, 255),
        ChatKind.Clan => Color.Rgb(168, 243, 115),
        ChatKind.Player => Color.Rgb(240, 240, 240),
        ChatKind.Notice => Color.Rgb(255, 214, 120),
        _ => MobileHudTheme.Ink
    };

    private static Color Name(ChatKind kind) => kind switch
    {
        ChatKind.Party => Color.Rgb(170, 238, 255),
        ChatKind.Whisper => Color.Rgb(255, 205, 255),
        ChatKind.Clan => Color.Rgb(205, 255, 150),
        ChatKind.Player => Color.Rgb(255, 222, 120),
        _ => Color.Rgb(220, 228, 226)
    };
}
