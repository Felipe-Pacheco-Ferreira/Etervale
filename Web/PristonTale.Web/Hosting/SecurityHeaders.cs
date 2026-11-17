using PristonTale.Web.Services;

namespace PristonTale.Web.Hosting;

// Public pages disallow scripts by default. The GM panel polls its own protected page, and the site's entry ("/", the
// game itself) starts WebAssembly and connects to the game server's WebSocket edge.
public sealed class SecurityHeaders(RequestDelegate next, WebSettings settings)
{
    public const string PlayPagePath = "/";

    // The former address of the play page; it redirects to PlayPagePath.
    public const string LegacyPlayPagePath = "/jogar";

    private const string SharedDirectives =
        "img-src 'self'; style-src 'self'; font-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; "
        + "upgrade-insecure-requests";

    public const string ContentSecurityPolicy = "default-src 'none'; " + SharedDirectives;
    private const string GmPolicy = "default-src 'none'; script-src 'self'; connect-src 'self'; " + SharedDirectives;

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";

    private readonly string _playPolicy = PlayPolicy([settings.Play.GameEndpoint, .. settings.Play.WorldList.Select(static world => world.Endpoint)],
        settings.Security.TurnstileEnabled);

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var (context, playPolicy) = ((HttpContext, string))state;
            Apply(context.Response.Headers, PolicyFor(context.Request.Path, playPolicy));
            return Task.CompletedTask;
        }, (context, _playPolicy));
        return next(context);
    }

    public static string PolicyFor(PathString path, string playPolicy)
    {
        if (!path.HasValue || path.Value == PlayPagePath)
        {
            return playPolicy;
        }

        return string.Equals(path.Value?.TrimEnd('/'), "/conta/gm", StringComparison.OrdinalIgnoreCase)
            ? GmPolicy
            : ContentSecurityPolicy;
    }

    // The play page's policy: scripts and WebAssembly from the site, fetches from the site and each world's edge: its
    // wss:// socket and, on the same host and port, the https:// /status the world list reads (browsers do not let a
    // wss source admit https).
    public static string PlayPolicy(params string[] gameEndpoints) => PlayPolicy(gameEndpoints, turnstile: false);

    // turnstile: the registration's Cloudflare check is on, so its script and its frame come from Cloudflare too.
    public static string PlayPolicy(string[] gameEndpoints, bool turnstile)
    {
        var origins = gameEndpoints
            .SelectMany(static address => Uri.TryCreate(address, UriKind.Absolute, out var endpoint)
                ? [endpoint.GetLeftPart(UriPartial.Authority), new UriBuilder(endpoint) { Scheme = Uri.UriSchemeHttps, Path = "" }.Uri.GetLeftPart(UriPartial.Authority)]
                : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal);
        var connect = string.Join(' ', ["'self'", .. origins]);
        var scripts = turnstile ? $"'self' 'wasm-unsafe-eval' {Turnstile.ScriptOrigin}" : "'self' 'wasm-unsafe-eval'";
        var frames = turnstile ? $"frame-src {Turnstile.ScriptOrigin}; " : "";
        return $"default-src 'none'; script-src {scripts}; connect-src {connect}; media-src 'self'; {frames}" + SharedDirectives;
    }

    public static void Apply(IHeaderDictionary headers, string policy = ContentSecurityPolicy)
    {
        headers.ContentSecurityPolicy = policy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
    }
}
