namespace PristonTale.Simulation.World.Calendar;

// One card of the calendar: an event's occurrence, or a day's (or the coming hours') boss calls together. First is the
// one its countdown counts to.
public sealed record CalendarEntry(CalendarEventKind Kind, IReadOnlyList<CalendarOccurrence> Occurrences)
{
    public CalendarOccurrence First => Occurrences[0];
}

public sealed record CalendarDay(DateOnly Day, IReadOnlyList<CalendarEntry> Entries);

// The calendar's pages from its occurrences (in EventSchedule's order), at the viewer's time and in the viewer's time zone:
// what runs now, what starts in the coming hours, and the coming days.
public static class CalendarPages
{
    public static readonly TimeSpan ComingWindow = TimeSpan.FromHours(24);
    public const int WeekDays = 7;

    public static List<CalendarOccurrence> Running(IReadOnlyList<CalendarOccurrence> occurrences, DateTimeOffset now)
    {
        var running = new List<CalendarOccurrence>();
        foreach (var occurrence in occurrences)
        {
            if (occurrence.RunningAt(now))
            {
                running.Add(occurrence);
            }
        }

        return running;
    }

    // What starts after now and within the window: one card per occurrence, the boss calls on one card.
    public static List<CalendarEntry> Coming(IReadOnlyList<CalendarOccurrence> occurrences, DateTimeOffset now, TimeSpan window) =>
        Collect(occurrences, occurrence => !occurrence.Always && occurrence.Start > now && occurrence.Start <= now + window);

    // The days after today, each with what starts on it in the zone: one card per occurrence, the day's boss calls on one
    // card; a day with nothing is left out.
    public static List<CalendarDay> Days(IReadOnlyList<CalendarOccurrence> occurrences, DateTimeOffset now, TimeZoneInfo zone, int days)
    {
        var today = LocalDay(now, zone);
        var pages = new List<CalendarDay>(days);
        for (var day = 1; day <= days; day++)
        {
            var date = today.AddDays(day);
            var entries = Collect(occurrences, occurrence => !occurrence.Always && occurrence.Start > now && LocalDay(occurrence.Start, zone) == date);
            if (entries.Count > 0)
            {
                pages.Add(new CalendarDay(date, entries));
            }
        }

        return pages;
    }

    public static DateOnly LocalDay(DateTimeOffset moment, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);

    private static List<CalendarEntry> Collect(IReadOnlyList<CalendarOccurrence> occurrences, Func<CalendarOccurrence, bool> shown)
    {
        var entries = new List<CalendarEntry>();
        List<CalendarOccurrence> calls = null;
        foreach (var occurrence in occurrences)
        {
            if (!shown(occurrence))
            {
                continue;
            }

            if (occurrence.Kind != CalendarEventKind.FieldBosses)
            {
                entries.Add(new CalendarEntry(occurrence.Kind, [occurrence]));
            }
            else if (calls is null)
            {
                calls = [occurrence];
                entries.Add(new CalendarEntry(occurrence.Kind, calls));
            }
            else
            {
                calls.Add(occurrence);
            }
        }

        return entries;
    }
}
