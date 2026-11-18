namespace PristonTale.Contracts.Social;

// The game server's bot link (docs/discord-bot.md): a loopback HTTP endpoint the Discord bot calls with the shared key
// as "Authorization: Bearer <key>". Bodies are JSON with web (camelCase) names.
public static class BotLinkPaths
{
    public const string Chat = "/bot/chat";
    public const string Command = "/bot/command";
}

// A Discord message for the trade channel.
public sealed record BotChatRequest(string Author, string Text);

// A game-master command from a Discord member; the server caps the level at its own BotLink:MaxGmLevel.
public sealed record BotCommandRequest(string UserId, string Name, int Level, string Line);

// Outcome is the GmCommandOutcome name: Unknown, NeedsWorld, Denied or Done.
public sealed record BotCommandResponse(string Outcome, IReadOnlyList<string> Replies);
