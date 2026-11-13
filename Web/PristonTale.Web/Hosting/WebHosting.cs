using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Assets;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.World;
using PristonTale.Web.Services;

namespace PristonTale.Web.Hosting;

public static class WebHosting
{
    // The sign-in and registration forms: their posts per address and minute.
    public const string FormPostsPolicy = "form-posts";
    public const string FormCookieName = "__Host-pa-form";
    public const string AndroidDownloadPath = "/download/android";

    private static readonly TimeSpan StaticFileAge = TimeSpan.FromDays(7);
    private const int Ipv6NetworkBytes = 8;

    public static void AddPristonWeb(this WebApplicationBuilder builder, WebSettings settings)
    {
        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        AddDatabase(services, settings.Database);
        services.AddSingleton<AccountAppService>();
        services.AddSingleton(new LoginThrottle(settings.Security.LoginFailuresPerAccount, settings.Security.LoginFailuresPerIp,
            settings.Security.LoginFailureWindowSeconds * 1000L));
        services.AddSingleton<RegistrationGate>();
        services.AddSingleton<ServerStatusMonitor>();
        services.AddHostedService(static provider => provider.GetRequiredService<ServerStatusMonitor>());
        services.AddSingleton(static _ => ClassTitles.Load(WebSettings.BesideExecutable(ClassTitles.FileName)));
        services.AddSingleton(static _ => NewsFeed.Load(WebSettings.BesideExecutable(NewsFeed.FileName)));
        services.AddSingleton<AndroidPackage>();
        services.AddSingleton<SiteData>();
        services.AddSingleton(static _ => new WorldCatalog());

        builder.WebHost.ConfigureKestrel(static options => options.AddServerHeader = false);
        services.AddDataProtection()
            .SetApplicationName("PristonAvalon.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(WebSettings.BesideExecutable(settings.Site.KeysPath)));
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => AccountSession.Configure(options, settings.Security));
        services.AddAuthorization();
        services.AddAntiforgery(static options =>
        {
            options.Cookie.Name = FormCookieName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
        });
        services.AddRazorPages(static options => options.Conventions.AuthorizePage("/Conta"));
        services.Configure<RouteOptions>(static options => options.LowercaseUrls = true);
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(settings.Site.HstsDays);
            options.IncludeSubDomains = true;
        });
        services.AddHttpsRedirection(options =>
        {
            options.HttpsPort = settings.Site.HttpsPort;
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
        });
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in settings.Site.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });
        AddRateLimits(services, settings.Security);
    }

    public static void UsePristonWeb(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler("/erro");
        app.UseHsts();
        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeaders>();
        app.UseStatusCodePagesWithReExecute("/erro/{0}");
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl =
                "public, max-age=" + (long)StaticFileAge.TotalSeconds
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapMethods(AndroidDownloadPath, [HttpMethods.Get, HttpMethods.Head], static (AndroidPackage package) => package.Current() is { } file
            ? Results.File(file.Path, AndroidPackage.ContentType, file.DownloadName, file.LastWrite, enableRangeProcessing: true)
            : Results.NotFound());
    }

    // The address a request came from (after the trusted proxies), for the limits.
    public static IPAddress AddressOf(HttpContext context) => LimitKey(context.Connection.RemoteIpAddress);

    // The key the per-address limits count under: an IPv4 address, or an IPv6 address's /64 network, since one
    // subscriber usually holds a whole /64 and could otherwise change address for every attempt.
    public static IPAddress LimitKey(IPAddress? address)
    {
        if (address is null)
        {
            return IPAddress.None;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address;
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6NetworkBytes, bytes.Length - Ipv6NetworkBytes);
        return new IPAddress(bytes);
    }

    private static void AddDatabase(IServiceCollection services, DatabaseSettings database)
    {
        if (database.IsPostgres)
        {
            services.AddPristonPostgres(database.Postgres, database.MaxPoolSize);
            return;
        }

        // The site reads the game server's database and never creates one: a wrong path must stop it, not open an empty file.
        var path = DataPath.Resolve(database.SqlitePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Database:SqlitePath '{path}' does not exist.");
        }

        services.AddPristonSqlite(path);
    }

    private static void AddRateLimits(IServiceCollection services, SecuritySettings security)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("Muitas requisições. Aguarde um minuto e tente de novo.", ct);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, IPAddress>(context =>
                RateLimitPartition.GetFixedWindowLimiter(AddressOf(context), _ => PerMinute(security.RequestsPerMinute)));
            options.AddPolicy(FormPostsPolicy, context => HttpMethods.IsPost(context.Request.Method)
                ? RateLimitPartition.GetFixedWindowLimiter(AddressOf(context), _ => PerMinute(security.FormPostsPerMinute))
                : RateLimitPartition.GetNoLimiter(IPAddress.None));
        });
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };
}
