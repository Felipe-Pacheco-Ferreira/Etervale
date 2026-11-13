using PristonTale.Simulation.World.Calendar;

namespace PristonTale.Web.Services;

// A boss call and the bosses it brings.
public sealed record BossCall(CalendarOccurrence Call, IReadOnlyList<CalendarBoss> Bosses);

// A field boss and the server hours it is called at.
public sealed record BossHours(CalendarBoss Boss, IReadOnlyList<int> Hours);

// The calendar as the site shows it, in the server's time zone. Boss calls come nearly every hour, so they are kept apart
// from the other events: the next few calls with their bosses, and each boss's hours; Coming and Days hold the rest.
public sealed record SiteCalendar(EventCalendar Calendar, IReadOnlyList<CalendarOccurrence> Running, IReadOnlyList<CalendarEntry> Coming,
    IReadOnlyList<CalendarDay> Days, IReadOnlyList<BossCall> NextCalls, IReadOnlyList<BossHours> Bosses, DateTimeOffset Now, TimeZoneInfo Zone)
{
    public const int CallsShown = 6;

    public static SiteCalendar Build(EventCalendar calendar, IReadOnlyList<ScheduledBoss> bosses, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(bosses);
        var events = calendar.Occurrences.Where(static occurrence => occurrence.Kind != CalendarEventKind.FieldBosses).ToList();
        var calls = calendar.Occurrences
            .Where(static occurrence => occurrence.Kind == CalendarEventKind.FieldBosses)
            .Take(CallsShown)
            .Select(call => new BossCall(call, calendar.BossesOf(call)))
            .ToList();
        var table = bosses.Take(EventSchedule.MaxBosses)
            .Select(static boss => new BossHours(boss.Boss, EventSchedule.Hours(boss.Hours)))
            .OrderBy(static boss => boss.Hours[0])
            .ThenBy(static boss => boss.Boss.Name, StringComparer.CurrentCulture)
            .ToList();
        return new SiteCalendar(calendar, CalendarPages.Running(events, now), CalendarPages.Coming(events, now, CalendarPages.ComingWindow),
            CalendarPages.Days(events, now, zone, CalendarPages.WeekDays), calls, table, now, zone);
    }
}
