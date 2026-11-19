using System.Globalization;
using System.Text;

namespace PristonTale.Gameplay.UI.Chat;

// The chat filter (ClientSettings.ChatFilter): offensive words in what players write show as asterisks. A word matches
// whole, ignoring case and accents, so "Puta" and "putá" match "puta" but "computador" does not.
public static class ChatCensor
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "arrombado", "arrombada", "babaca", "bosta", "buceta", "caralho", "cacete", "corno", "cu", "cuzao", "desgracado", "desgracada",
        "fdp", "foda", "foder", "fodase", "idiota", "imbecil", "merda", "otario", "otaria", "porra", "puta", "puto", "vadia", "viado",
        "vagabundo", "vagabunda", "pqp", "vsf", "tnc", "krl", "porcaria",
        "asshole", "bastard", "bitch", "cunt", "dick", "fuck", "fucker", "fucking", "motherfucker", "shit", "slut", "whore", "wtf"
    };

    // A chat line's message after its "Name: " (or "From> Name: "), so a speaker's name stays readable and repliable.
    public static string MaskMessage(string line)
    {
        var colon = line?.IndexOf(": ", StringComparison.Ordinal) ?? -1;
        return colon < 0 ? Mask(line) : string.Concat(line.AsSpan(0, colon + 2), Mask(line[(colon + 2)..]));
    }

    public static string Mask(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        StringBuilder masked = null;
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var letter = index < text.Length && char.IsLetterOrDigit(text[index]);
            if (letter && start < 0)
            {
                start = index;
            }
            else if (!letter && start >= 0)
            {
                if (Words.Contains(Plain(text.AsSpan(start, index - start))))
                {
                    masked ??= new StringBuilder(text);
                    for (var at = start; at < index; at++)
                    {
                        masked[at] = '*';
                    }
                }

                start = -1;
            }
        }

        return masked?.ToString() ?? text;
    }

    // Lower case without accents.
    private static string Plain(ReadOnlySpan<char> word)
    {
        var decomposed = word.ToString().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var letter in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(letter);
            }
        }

        return plain.ToString();
    }
}
