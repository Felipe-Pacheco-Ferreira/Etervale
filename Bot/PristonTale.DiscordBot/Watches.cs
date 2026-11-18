using System.Globalization;
using PristonTale.DiscordBot.Sources;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// Worlds going down or coming back. The first reading only sets what each world is; a change is announced once two
// readings in a row agree, so one lost status request does not post a fall.
public sealed class WorldWatch
{
    private readonly Dictionary<string, bool> _announced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _pending = new(StringComparer.Ordinal);

    public IReadOnlyList<WorldReport> Changes(IReadOnlyList<WorldReport> reports)
    {
        var changes = new List<WorldReport>();
        foreach (var report in reports)
        {
            var name = report.World.Name;
            if (!_announced.TryGetValue(name, out var announced))
            {
                _announced[name] = report.Online;
                continue;
            }

            if (announced == report.Online)
            {
                _pending.Remove(name);
            }
            else if (_pending.TryGetValue(name, out var pending) && pending == report.Online)
            {
                _announced[name] = report.Online;
                _pending.Remove(name);
                changes.Add(report);
            }
            else
            {
                _pending[name] = report.Online;
            }
        }

        return changes;
    }
}

// Events and boss calls that started since the last look. The first look only marks what already runs.
public sealed class EventWatch
{
    private HashSet<string>? _running;

    public IReadOnlyList<CalendarOccurrence> Started(SiteCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var now = calendar.Now;
        var running = calendar.Calendar.Occurrences.Where(occurrence => !occurrence.Always && occurrence.RunningAt(now)).ToArray();
        var keys = running.Select(Key).ToHashSet(StringComparer.Ordinal);
        var started = _running is null ? [] : running.Where(occurrence => !_running.Contains(Key(occurrence))).ToArray();
        _running = keys;
        return started;
    }

    private static string Key(CalendarOccurrence occurrence) =>
        occurrence.Kind + "@" + occurrence.Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}

// News posts not announced yet, oldest first; none on the first read (no list in the state), which marks every post.
// Posts are marked only after they were sent, so a failed send announces them on the next read.
public static class NewsWatch
{
    public static IReadOnlyList<NewsItem> Fresh(BotState state, IReadOnlyList<NewsItem> items)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.News is null ? [] : items.Where(item => !state.News.Contains(Key(item))).Reverse().ToArray();
    }

    public static void Mark(BotState state, IEnumerable<NewsItem> items)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.News ??= [];
        state.News.AddRange(items.Select(Key).Where(key => !state.News.Contains(key)).ToArray());
        if (state.News.Count > BotState.MostNews)
        {
            state.News.RemoveRange(0, state.News.Count - BotState.MostNews);
        }
    }

    private static string Key(NewsItem item) => item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + item.Title;
}
