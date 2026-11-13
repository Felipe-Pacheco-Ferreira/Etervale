using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Application.Settings;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

public sealed class IndexModel(SiteData data, NewsFeed news, ServerStatusMonitor monitor, AndroidPackage android, TimeProvider time,
    ILogger<IndexModel> logger) : PageModel
{
    private const int NewsShown = 3;
    private const int ComingShown = 3;
    private const int TopShown = 5;

    public ServerStatus? Status { get; private set; }
    public WorldTotals Totals { get; private set; } = new(0, 0);
    public ServerSettings Server { get; private set; } = ServerSettings.Default;
    public IReadOnlyList<NewsItem> News { get; private set; } = [];
    public SiteCalendar? Calendar { get; private set; }
    public IReadOnlyList<RankedCharacter> Top { get; private set; } = [];
    public bool AndroidAvailable { get; private set; }
    public string AndroidVersion { get; private set; } = "";
    public DateTimeOffset Now { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Now = time.GetUtcNow();
        Status = monitor.Current;
        Totals = await data.TotalsAsync(ct);
        Server = await data.SettingsAsync(ct);
        News = news.Items(logger).Take(NewsShown).ToList();
        Calendar = await data.CalendarAsync(ct);
        Top = (await data.TopAsync(null, ct)).Take(TopShown).ToList();
        AndroidAvailable = android.IsAvailable;
        AndroidVersion = android.Version;
    }

    public IEnumerable<Simulation.World.Calendar.CalendarEntry> Coming => Calendar?.Coming.Take(ComingShown) ?? [];
}
