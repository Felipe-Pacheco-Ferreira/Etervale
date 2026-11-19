using PristonTale.Application.GameMasters;
using PristonTale.Contracts.Social;
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
    public DevNoteSettings DevNotes { get; init; } = new();

    // The game servers the chat channels follow, each named in Discord. Empty: the one server of GameLink and Activity.
    public List<GameServerSettings> Servers { get; init; } = [];

    // Every game server the bot follows; the first with a link answers /gm.
    public IReadOnlyList<GameServerSettings> GameServers =>
        Servers.Count > 0 ? Servers : [new GameServerSettings { GameLink = GameLink, Activity = Activity }];

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

        if (Servers.Count > 0 && (Servers.Any(static server => string.IsNullOrWhiteSpace(server.Name))
            || Servers.Select(static server => server.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Servers.Count))
        {
            problems.Add("Servers: every server needs a Name of its own.");
        }

        foreach (var server in GameServers)
        {
            var prefix = server.Name.Length > 0 ? $"Servers ({server.Name}): GameLink" : "GameLink";
            if (server.GameLink.Url.Length > 0 && (!Uri.TryCreate(server.GameLink.Url, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttp || !link.IsLoopback))
            {
                problems.Add($"{prefix}:Url must be the game server's BotLink:Url (http:// on loopback).");
            }

            if (server.GameLink.Url.Length > 0 && server.GameLink.Key.Length == 0)
            {
                problems.Add($"{prefix}:Key is empty.");
            }
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

        if (Discord.DevNoteMinGmLevel is < GameMasterLevel.Guide or > GameMasterLevel.Highest)
        {
            problems.Add("Discord:DevNoteMinGmLevel must be 1 to 4.");
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

    // Announcements, world status and development notes ping @everyone; false posts them silently.
    public bool PingEveryone { get; init; } = true;

    // The game-master level a member needs to post a development note with /devnota.
    public int DevNoteMinGmLevel { get; init; } = GameMasterLevel.Administrator;

    public ulong TicketChannel(string category) => TicketChannels.TryGetValue(category, out var channel) && channel != 0 ? channel : Channels.Tickets;
}

// Channel ids; 0 leaves that feature off.
public sealed record ChannelSettings
{
    // Server up and down, news and events starting.
    public ulong Announcements { get; init; }

    // Worlds going down and up; 0 sends them to Announcements.
    public ulong WorldStatus { get; init; }

    public ulong WorldStatusChannel => WorldStatus != 0 ? WorldStatus : Announcements;

    // The game's global chat: the spoken and notice lines of every server. Read only.
    public ulong PublicChat { get; init; }

    // The game's trade channel of every server; the members' messages here go to the trade channel of every server.
    public ulong TradeChat { get; init; }

    // Whispers and party lines (and clan lines while ClanChat is 0): a channel only game masters can see.
    public ulong PrivateChat { get; init; }

    // Clan lines of every server: a channel only game masters can see. 0 sends them to PrivateChat.
    public ulong ClanChat { get; init; }

    // The channel a line of the game's private activity goes to.
    public ulong PrivateChannel(string channel) => channel == "clan" && ClanChat != 0 ? ClanChat : PrivateChat;

    // Bosses slain (who, the party, the drops) and players killed by players, as cards, from every server.
    public ulong BossKills { get; init; }
    public ulong PvpKills { get; init; }

    public ulong FeatChannel(string kind) => kind switch
    {
        GameFeat.Boss => BossKills,
        GameFeat.Pvp => PvpKills,
        _ => 0
    };

    // Development notes posted with /devnota.
    public ulong Development { get; init; }

    // Development notes of staff tools (Tipo: equipe), for game masters only.
    public ulong StaffDevelopment { get; init; }

    // Everything game masters do (gm_audit: the game's commands, the site's panel and answers, the bot's actions), for
    // game masters only.
    public ulong GmAudit { get; init; }

    // Support tickets without a channel of their category in Discord:TicketChannels: a channel only game masters can see.
    public ulong Tickets { get; init; }
}

public sealed record SiteSettings
{
    // The website, for /api/worlds (each world's /status) and /api/client/news.
    public string BaseUrl { get; init; } = "https://localhost:6443";
}

// One game server: its name in Discord's lines (empty for the only one), its bot link and its activity snapshots.
public sealed record GameServerSettings
{
    public string Name { get; init; } = "";
    public GameLinkSettings GameLink { get; init; } = new();
    public ActivitySettings Activity { get; init; } = new();
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

// The development notes the bot publishes by itself: the devnotes folder built beside the executable, the site's news
// file the game reads them from (empty: Discord only) and the name they are signed with.
public sealed record DevNoteSettings
{
    public string Path { get; init; } = "devnotes";
    public string NewsPath { get; init; } = "";
    public string Author { get; init; } = "Equipe Etervale";
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
