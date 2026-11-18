using PristonTale.Application.GameMasters;

namespace PristonTale.Server.Net;

// BotLink: the loopback endpoint the Discord bot uses to speak in the trade channel and run remote game-master commands
// (docs/discord-bot.md). Off unless Enabled.
internal sealed record BotLinkSettings
{
    public const string Section = "BotLink";
    public const int MinimumKeyLength = 32;

    public bool Enabled { get; init; }

    // Kestrel's listen URL; plain http on a loopback address only, since the key travels in a header.
    public string Url { get; init; } = "http://127.0.0.1:10011";

    // The shared secret the bot sends as a bearer token; kept in the settings file beside the executable.
    public string Key { get; init; } = "";

    // The highest game-master level a bot request may act with, whatever level the bot claims for a member.
    public int MaxGmLevel { get; init; } = GameMasterLevel.Moderator;

    public static BotLinkSettings From(IConfiguration configuration)
    {
        var settings = configuration.GetSection(Section).Get<BotLinkSettings>() ?? new BotLinkSettings();
        if (!settings.Enabled)
        {
            return settings;
        }

        Require(Uri.TryCreate(settings.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp && url.IsLoopback,
            "Url must be an http:// URL on a loopback address.");
        Require(settings.Key.Length >= MinimumKeyLength, $"Key must have at least {MinimumKeyLength} characters.");
        Require(settings.MaxGmLevel is >= GameMasterLevel.Player and <= GameMasterLevel.Administrator, "MaxGmLevel must be 0 to 4.");
        return settings;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{Section}: {message}");
        }
    }
}
