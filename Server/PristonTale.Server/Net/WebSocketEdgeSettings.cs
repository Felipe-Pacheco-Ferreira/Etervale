using System.Net;
using PristonTale.Assets;

namespace PristonTale.Server.Net;

// Network:WebSocket: the optional WebSocket edge for browser clients (docs/server.md). Off unless Enabled.
internal sealed record WebSocketEdgeSettings
{
    public const string Section = "Network:WebSocket";
    public const string DefaultPath = "/game";

    public bool Enabled { get; init; }

    // Kestrel's listen URL: "https://host:port" serves wss with Certificate (or Kestrel's default development
    // certificate when no path is set); "http://127.0.0.1:port" serves plain ws for a reverse proxy that terminates TLS.
    public string Url { get; init; } = "https://localhost:10010";
    public string LanAddress { get; init; } = "";

    public string Path { get; init; } = DefaultPath;

    // The pages allowed to open the socket ("https://site.example"); a browser request from any other origin is refused.
    // Clients that send no Origin header (desktop tools) are not browsers and are admitted.
    public string[] AllowedOrigins { get; init; } = [];

    // Reverse proxies whose X-Forwarded-For is believed, so the per-address limits count the player; loopback always is.
    public string[] KnownProxies { get; init; } = [];

    public string CertificatePath { get; init; } = "";

    public string CertificatePassword { get; init; } = "";

    public static WebSocketEdgeSettings From(IConfiguration configuration)
    {
        var settings = configuration.GetSection(Section).Get<WebSocketEdgeSettings>() ?? new WebSocketEdgeSettings();
        if (!settings.Enabled)
        {
            return settings;
        }

        Require(Uri.TryCreate(settings.Url, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps),
            "Url must be an http:// or https:// URL.");
        // Logins carry the password: plain ws may only reach a proxy on this machine that terminates TLS.
        Require(url!.Scheme == Uri.UriSchemeHttps || url.IsLoopback, "an http:// Url must listen on loopback (behind a proxy that terminates TLS).");
        if (settings.LanAddress.Length > 0)
        {
            Require(url.Scheme == Uri.UriSchemeHttps && url.IsLoopback &&
                IPAddress.TryParse(settings.LanAddress, out var lan) && !IPAddress.IsLoopback(lan) &&
                !lan.Equals(IPAddress.Any) && !lan.Equals(IPAddress.IPv6Any),
                "LanAddress requires HTTPS, a loopback Url, and a specific non-loopback IP.");
        }
        Require(settings.Path.StartsWith('/'), "Path must start with '/'.");
        foreach (var origin in settings.AllowedOrigins)
        {
            Require(OriginOf(origin) is { } parsed && string.Equals(parsed, origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase),
                $"AllowedOrigins has an entry that is not an origin as browsers send it (scheme://host[:port], no default port): '{origin}'.");
        }

        foreach (var proxy in settings.KnownProxies)
        {
            Require(IPAddress.TryParse(proxy, out _), $"KnownProxies has an entry that is not an address: '{proxy}'.");
        }

        return settings;
    }

    // The origin a browser sends for the address, or null when it is not an http(s) address without user, path or query.
    private static string? OriginOf(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && parsed.UserInfo.Length == 0 && parsed.AbsolutePath == "/" && parsed.Query.Length == 0 && parsed.Fragment.Length == 0
            ? parsed.GetLeftPart(UriPartial.Authority)
            : null;

    public string? ResolvedCertificatePath => CertificatePath.Length == 0 ? null : DataPath.Resolve(CertificatePath);

    public bool AllowsOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        foreach (var allowed in AllowedOrigins)
        {
            if (string.Equals(allowed.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{Section}: {message}");
        }
    }
}
