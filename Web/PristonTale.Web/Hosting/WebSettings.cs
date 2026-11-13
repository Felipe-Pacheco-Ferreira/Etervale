using Microsoft.Extensions.Configuration;

namespace PristonTale.Web.Hosting;

public sealed record SiteSettings
{
    public string Name { get; init; } = "Priston Avalon";

    // The public HTTPS port the HTTP endpoint redirects to.
    public int HttpsPort { get; init; } = 443;

    public int HstsDays { get; init; } = 365;

    // Where the keys that protect the session and form cookies are kept, relative to the executable.
    public string KeysPath { get; init; } = "keys";

    // Reverse proxies whose X-Forwarded-For and X-Forwarded-Proto are believed; loopback always is.
    public string[] KnownProxies { get; init; } = [];
}

public sealed record DatabaseSettings
{
    public string Provider { get; init; } = "Sqlite";

    // The game server's SQLite file; a relative path resolves like the server's (DataPath.Resolve).
    public string SqlitePath { get; init; } = Path.Combine("data", "pristontale.db");

    public string Postgres { get; init; } = "";

    public int MaxPoolSize { get; init; } = 10;

    public bool IsSqlite => string.Equals(Provider, "Sqlite", StringComparison.OrdinalIgnoreCase);

    public bool IsPostgres => string.Equals(Provider, "Postgres", StringComparison.OrdinalIgnoreCase);
}

public sealed record GameServerSettings
{
    // Where the site checks that the game server accepts connections.
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 10009;

    public int ProbeSeconds { get; init; } = 30;
}

public sealed record DownloadSettings
{
    // The Android package offered for download, relative to the executable; the page says "soon" while it is missing.
    public string AndroidApkPath { get; init; } = Path.Combine("downloads", "priston-avalon.apk");

    public string AndroidVersion { get; init; } = "";
}

public sealed record SecuritySettings
{
    public int LoginFailuresPerAccount { get; init; } = 5;

    public int LoginFailuresPerIp { get; init; } = 20;

    public int LoginFailureWindowSeconds { get; init; } = 300;

    public int RegistrationsPerIp { get; init; } = 3;

    public int RegistrationWindowMinutes { get; init; } = 60;

    // Requests of one address per minute, any page.
    public int RequestsPerMinute { get; init; } = 120;

    // Posts of the sign-in and registration forms of one address per minute.
    public int FormPostsPerMinute { get; init; } = 10;

    public int SessionMinutes { get; init; } = 120;
}

// The settings of web-settings.json beside the executable and of the command line (--Section:Key=value).
public sealed record WebSettings(SiteSettings Site, DatabaseSettings Database, GameServerSettings GameServer, DownloadSettings Downloads,
    SecuritySettings Security)
{
    public const string FileName = "web-settings.json";

    public static WebSettings Read(IConfiguration configuration)
    {
        var settings = new WebSettings(
            configuration.GetSection("Site").Get<SiteSettings>() ?? new SiteSettings(),
            configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings(),
            configuration.GetSection("GameServer").Get<GameServerSettings>() ?? new GameServerSettings(),
            configuration.GetSection("Downloads").Get<DownloadSettings>() ?? new DownloadSettings(),
            configuration.GetSection("Security").Get<SecuritySettings>() ?? new SecuritySettings());
        settings.Validate();
        return settings;
    }

    // A path of the settings relative to the executable's directory.
    public static string BesideExecutable(string path) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    public void Validate()
    {
        Require(!string.IsNullOrWhiteSpace(Site.Name), "Site:Name is required.");
        Require(Site.HttpsPort is > 0 and <= 65535, "Site:HttpsPort must be 1 to 65535.");
        Require(Site.HstsDays >= 0, "Site:HstsDays must not be negative.");
        Require(!string.IsNullOrWhiteSpace(Site.KeysPath), "Site:KeysPath is required.");
        foreach (var proxy in Site.KnownProxies)
        {
            Require(System.Net.IPAddress.TryParse(proxy, out _), $"Site:KnownProxies has an address that is not one: '{proxy}'.");
        }

        Require(Database.IsSqlite || Database.IsPostgres, "Database:Provider must be Sqlite or Postgres.");
        Require(!Database.IsSqlite || !string.IsNullOrWhiteSpace(Database.SqlitePath), "Database:SqlitePath is required for Sqlite.");
        Require(!Database.IsPostgres || !string.IsNullOrWhiteSpace(Database.Postgres), "Database:Postgres is required for Postgres.");
        Require(Database.MaxPoolSize >= 1, "Database:MaxPoolSize must be at least 1.");
        Require(!string.IsNullOrWhiteSpace(GameServer.Host), "GameServer:Host is required.");
        Require(GameServer.Port is > 0 and <= 65535, "GameServer:Port must be 1 to 65535.");
        Require(GameServer.ProbeSeconds >= 5, "GameServer:ProbeSeconds must be at least 5.");
        Require(!string.IsNullOrWhiteSpace(Downloads.AndroidApkPath), "Downloads:AndroidApkPath is required.");
        Require(Security.LoginFailuresPerAccount >= 1 && Security.LoginFailuresPerIp >= 1 && Security.LoginFailureWindowSeconds >= 1,
            "Security: the login failure limits and window must be at least 1.");
        Require(Security.RegistrationsPerIp >= 1 && Security.RegistrationWindowMinutes >= 1,
            "Security: the registration limit and window must be at least 1.");
        Require(Security.RequestsPerMinute >= 1 && Security.FormPostsPerMinute >= 1, "Security: the request limits must be at least 1.");
        Require(Security.SessionMinutes is >= 5 and <= 7 * 24 * 60, "Security:SessionMinutes must be 5 to 10080.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
