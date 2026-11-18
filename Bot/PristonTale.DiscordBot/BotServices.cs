using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Support;
using PristonTale.DiscordBot.Sources;
using PristonTale.Simulation.World;
using PristonTale.Web.Hosting;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

public static class BotServices
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

    // Everything the bot reads, shared by the Discord service and the console's --check.
    public static void Add(IServiceCollection services, BotSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        WebHosting.AddDatabase(services, settings.Database);
        services.AddSingleton(static _ => new WorldCatalog());
        services.AddSingleton<SiteData>();
        services.AddSingleton(static _ => ClassTitles.Load(BotSettings.BesideExecutable(ClassTitles.FileName)));
        if (settings.Tickets.Path.Length > 0)
        {
            services.AddSingleton(new SupportTickets(BotSettings.BesideExecutable(settings.Tickets.Path)));
        }

        services.AddSingleton(_ => new SiteClient(Http(new Uri(settings.Site.BaseUrl))));
        services.AddSingleton(_ => new GameLinkClient(Http(null), settings.GameLink));
        services.AddSingleton<BotQueries>();
    }

    // One client for the bot's whole run; pooled connections are renewed so a changed address is picked up.
    private static HttpClient Http(Uri? baseAddress) =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime })
        {
            BaseAddress = baseAddress,
            Timeout = HttpTimeout
        };

    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);
}
