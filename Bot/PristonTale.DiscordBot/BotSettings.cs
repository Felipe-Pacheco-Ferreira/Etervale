using PristonTale.Application.GameMasters;
using PristonTale.Web.Hosting;

namespace PristonTale.DiscordBot;

// discord-bot-settings.json beside the executable, then the command line (--Discord:Token=... and so on). The bot reads
// no environment variable.
public sealed record BotSettings
{
    public const string FileName = "discord-bot-settings.json";

    public DiscordSettings Discord { get; init; } = new();
    public SiteSettings Site { get; init; } = new();
    public DatabaseSettings Database { get; init; } = new();
    public GameLinkSettings GameLink { get; init; } = new();
    public ActivitySettings Activity { get; init; } = new();
    public TicketSettings Tickets { get; init; } = new();
    public WatchSettings Watch { get; init; } = new();

    // A file path of the settings, resolved beside the executable like every other path the bot reads.
    public static string BesideExecutable(string path) => WebSettings.BesideExecutable(path);

    // The problems that stop the start; empty when the settings are usable. A Discord token is required only to connect.
    public IReadOnlyList<string> Problems(bool connecting)
    {
        var problems = new List<string>();
        if (connecting && string.IsNullOrWhiteSpace(Discord.Token))
        {
            problems.Add("Discord:Token is empty.");
        }

        if (connecting && Discord.GuildId == 0)
        {
            problems.Add("Discord:GuildId is 0.");
        }

        if (!Uri.TryCreate(Site.BaseUrl, UriKind.Absolute, out var site) || site.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add("Site:BaseUrl must be an https:// address.");
        }

        if (GameLink.Url.Length > 0 && (!Uri.TryCreate(GameLink.Url, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttp || !link.IsLoopback))
        {
            problems.Add("GameLink:Url must be the game server's BotLink:Url (http:// on loopback).");
        }

        if (GameLink.Url.Length > 0 && GameLink.Key.Length == 0)
        {
            problems.Add("GameLink:Key is empty.");
        }

        foreach (var (role, level) in Discord.GameMasterRoles)
        {
            if (!ulong.TryParse(role, out _) || level is < GameMasterLevel.Guide or > GameMasterLevel.Highest)
            {
                problems.Add($"Discord:GameMasterRoles: '{role}' must be a role id with a level from 1 to 4.");
            }
        }

        if (Tickets.MinGmLevel is < GameMasterLevel.Guide or > GameMasterLevel.Highest)
        {
            problems.Add("Tickets:MinGmLevel must be 1 to 4.");
        }

        if (Watch.PollSeconds < 1 || Watch.StatusSeconds < 5 || Watch.NewsSeconds < 30)
        {
            problems.Add("Watch: PollSeconds must be at least 1, StatusSeconds at least 5 and NewsSeconds at least 30.");
        }

        return problems;
    }
}

public sealed record DiscordSettings
{
    public string Token { get; init; } = "";
    public ulong GuildId { get; init; }
    public ChannelSettings Channels { get; init; } = new();

    // Role id -> game-master level (1 Guide to 4 Administrator); a member acts with the highest level of its roles.
    public Dictionary<string, int> GameMasterRoles { get; init; } = [];

    // Ticket category (Bug, Melhoria, Outro) -> the text channel its tickets go to; others go to Channels:Tickets.
    public Dictionary<string, ulong> TicketChannels { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ulong TicketChannel(string category) => TicketChannels.TryGetValue(category, out var channel) && channel != 0 ? channel : Channels.Tickets;
}

// Channel ids; 0 leaves that feature off.
public sealed record ChannelSettings
{
    // Server up and down, news and events starting.
    public ulong Announcements { get; init; }

    // Spoken, trade and notice lines of the game; the members' messages here go to the game's trade channel.
    public ulong PublicChat { get; init; }

    // Whispers, party and clan lines: a channel only game masters can see.
    public ulong PrivateChat { get; init; }

    // Support tickets without a channel of their category in Discord:TicketChannels: a channel only game masters can see.
    public ulong Tickets { get; init; }
}

public sealed record SiteSettings
{
    // The website, for /api/worlds (each world's /status) and /api/client/news.
    public string BaseUrl { get; init; } = "https://localhost:6443";
}

// The game server's bot link (its BotLink:Url and BotLink:Key); empty turns off Discord-to-game chat and /gm.
public sealed record GameLinkSettings
{
    public string Url { get; init; } = "";
    public string Key { get; init; } = "";
}

// The game server's activity snapshots (its PublicActivity:Path and PublicActivity:WhisperPath); empty turns a relay off.
public sealed record ActivitySettings
{
    public string PublicPath { get; init; } = "";
    public string PrivatePath { get; init; } = "";
}

// The support tickets' folder, the same as the site's Site:SupportTicketsPath and the server's SupportTickets:Path.
public sealed record TicketSettings
{
    public string Path { get; init; } = "";

    // The game-master level a member needs to answer a ticket.
    public int MinGmLevel { get; init; } = GameMasterLevel.Guide;
}

public sealed record WatchSettings
{
    // Chat relays and tickets.
    public int PollSeconds { get; init; } = 5;

    // The worlds' status and the events starting.
    public int StatusSeconds { get; init; } = 30;

    public int NewsSeconds { get; init; } = 120;

    // Members' messages in the public chat channel go to the game.
    public bool RelayToGame { get; init; } = true;
}
