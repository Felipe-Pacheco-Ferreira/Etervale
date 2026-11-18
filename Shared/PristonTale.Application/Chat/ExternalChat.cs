using System.Text;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Social;

namespace PristonTale.Application.Chat;

// Lines that come into the game from outside it (the Discord bridge): they go to every player on the trade channel as
// "[source] author: text". The bridge reads the trade channel back from the public activity stream and leaves out the
// lines carrying its own prefix, so nothing it sent comes back to it.
public static class ExternalChat
{
    public const int AuthorLimit = 24;

    // The source of the Discord bot's lines (the game server's bot link).
    public const string DiscordSource = "Discord";

    public static string Prefix(string source) => "[" + source + "] ";

    public static bool IsFrom(string source, string line) => line.StartsWith(Prefix(source), StringComparison.Ordinal);

    // The line as players receive it, or "" when nothing printable is left. The text is kept to the chat input's length.
    public static string Compose(string source, string author, string text)
    {
        var name = SingleLine(author, AuthorLimit);
        var message = SingleLine(text, ChatRules.InputLimit);
        return name.Length == 0 || message.Length == 0 ? "" : ChatRules.ClipToCapacity(ChatRules.Compose(Prefix(source) + name, message));
    }

    // Each author passes the players' flood rules (ChatFloodGuard), as a trade line would.
    public static ExternalChatOutcome Relay(ChatDirectory directory, ExternalChatFlood flood, long nowMs, string source, string author, string text)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(flood);
        var line = Compose(source, author, text);
        if (line.Length == 0)
        {
            return ExternalChatOutcome.Empty;
        }

        if (!flood.Allows(author, line, nowMs))
        {
            return ExternalChatOutcome.Flooded;
        }

        directory.Broadcast(new ChatLine(ChatKind.Trade, 0, line));
        return ExternalChatOutcome.Sent;
    }

    // Control characters and line breaks become single spaces; the result is trimmed and cut to the limit.
    private static string SingleLine(string text, int limit)
    {
        var builder = new StringBuilder(Math.Min(text.Length, limit));
        foreach (var character in text)
        {
            if (builder.Length == limit || (char.IsHighSurrogate(character) && builder.Length + 1 == limit))
            {
                break;
            }

            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }
}

public enum ExternalChatOutcome
{
    Sent,

    // Nothing printable was left.
    Empty,

    // The author broke the flood rules.
    Flooded
}

// A flood guard per outside author. Thread-safe; the guards are forgotten when too many authors were seen, which only
// lets a returning author start afresh.
public sealed class ExternalChatFlood
{
    private const int MostAuthors = 1000;
    private readonly Dictionary<string, ChatFloodGuard> _guards = new(StringComparer.OrdinalIgnoreCase);

    public bool Allows(string author, string line, long nowMs)
    {
        lock (_guards)
        {
            if (!_guards.TryGetValue(author, out var guard))
            {
                if (_guards.Count >= MostAuthors)
                {
                    _guards.Clear();
                }

                guard = new ChatFloodGuard();
                _guards[author] = guard;
            }

            return guard.Check(line, command: false, whisper: false, nowMs) == ChatFloodVerdict.Allowed;
        }
    }
}
