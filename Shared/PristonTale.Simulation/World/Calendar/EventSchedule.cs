using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Simulation.World.Calendar;

// The timed events the event calendar lists; a wire code: append only.
public enum CalendarEventKind : byte
{
    FieldBosses = 1,
    Bellatra = 2,
    CastleSiege = 3,
    CrystalEvent = 4,
    ExperienceEvent = 5,
    DropEvents = 6
}

[Flags]
public enum CalendarOccurrenceFlags : byte
{
    None = 0,

    // Runs the whole time, with no start or end.
    Always = 1,

    // A boss call on a later server day: that day's minute is drawn on the day (BossSpawnSchedule), so only the hour is
    // known and Start is the hour's start.
    MinuteUnknown = 2
}

// A field boss: its name and its field's.
public sealed record CalendarBoss(string Name, string FieldName);

// A field boss and the server-local hours it is called at, bit h for hour h (0 to 23).
public sealed record ScheduledBoss(CalendarBoss Boss, int Hours);

public readonly record struct CalendarWindow(DateTimeOffset Start, DateTimeOffset End);

// One running or coming event in UTC, from Start until End; a moment (a boss call) ends where it starts, and an Always
// event has neither. Bosses: the field bosses a boss call brings, bit i for the calendar's boss i.
public readonly record struct CalendarOccurrence(CalendarEventKind Kind, DateTimeOffset Start, DateTimeOffset End,
    CalendarOccurrenceFlags Flags = CalendarOccurrenceFlags.None, ulong Bosses = 0)
{
    public bool Always => (Flags & CalendarOccurrenceFlags.Always) != 0;

    public bool MinuteUnknown => (Flags & CalendarOccurrenceFlags.MinuteUnknown) != 0;

    public bool RunningAt(DateTimeOffset now) => Always || Start <= now && now < End;
}

// What the event calendar is made from: the server's settings and today's boss minute.
public sealed record EventScheduleRules
{
    // At most EventSchedule.MaxBosses; later ones are left out.
    public IReadOnlyList<ScheduledBoss> Bosses { get; init; } = [];

    // The minute of today's boss calls, today being the server's local day.
    public int BossMinute { get; init; }

    // False when today's minute is not known (a reader outside the game server): today's calls are then shown by their hour only.
    public bool BossMinuteKnown { get; init; } = true;

    public BellatraMode Bellatra { get; init; }

    public CastleMode Castle { get; init; }

    public int SiegeDay { get; init; }

    public int SiegeHour { get; init; }

    // The siege running now, null when none runs: it replaces the scheduled one, and a scheduled siege the castle does
    // not report running (ended early) is left out.
    public CalendarWindow? LiveSiege { get; init; }

    // The crystal event's mode (MonsterCrystals.EventAfternoons, EventAllDay; 0 off).
    public int CrystalEvent { get; init; }

    // The experience event's percent in force (KillReward.EventPercent): 100 is none.
    public int ExperiencePercent { get; init; } = KillReward.NoEventPercent;

    public DropEvents Drops { get; init; }
}

// The calendar as sent to a player: the bosses the boss calls name and the occurrences.
public sealed record EventCalendar(IReadOnlyList<CalendarBoss> Bosses, IReadOnlyList<CalendarOccurrence> Occurrences, int ExperiencePercent,
    DropEvents Drops)
{
    // The bosses a boss call brings (its Bosses bits).
    public List<CalendarBoss> BossesOf(CalendarOccurrence call)
    {
        var named = new List<CalendarBoss>();
        for (var index = 0; index < Bosses.Count && index < EventSchedule.MaxBosses; index++)
        {
            if ((call.Bosses & 1UL << index) != 0)
            {
                named.Add(Bosses[index]);
            }
        }

        return named;
    }
}

// The timed events of the server's rules over a span of time. Hours are the server's local hours (its time zone); the
// occurrences are in UTC. A local time a daylight-saving change skips has no occurrence.
public static class EventSchedule
{
    public const int MaxBosses = 64;
    private const int HoursPerDay = 24;

    // The calendar's span: a week after the current day.
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(8);

    // The bit set of hours 0 to 23 among those given (others never come).
    public static int HourMask(IEnumerable<int> hours)
    {
        var mask = 0;
        foreach (var hour in hours)
        {
            if (hour is >= 0 and < HoursPerDay)
            {
                mask |= 1 << hour;
            }
        }

        return mask;
    }

    // The hours of a HourMask, in order.
    public static List<int> Hours(int mask)
    {
        var hours = new List<int>();
        for (var hour = 0; hour < HoursPerDay; hour++)
        {
            if ((mask & 1 << hour) != 0)
            {
                hours.Add(hour);
            }
        }

        return hours;
    }

    public static EventCalendar Calendar(EventScheduleRules rules, DateTimeOffset now, TimeZoneInfo zone, TimeSpan horizon)
    {
        var bosses = new List<CalendarBoss>(Math.Min(rules.Bosses.Count, MaxBosses));
        for (var index = 0; index < rules.Bosses.Count && index < MaxBosses; index++)
        {
            bosses.Add(rules.Bosses[index].Boss);
        }

        return new EventCalendar(bosses, Occurrences(rules, now, zone, horizon), rules.ExperiencePercent, rules.Drops);
    }

    // The events running at now or starting before now + horizon: those always running first, then by start.
    public static List<CalendarOccurrence> Occurrences(EventScheduleRules rules, DateTimeOffset now, TimeZoneInfo zone, TimeSpan horizon)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(zone);
        var reach = new Reach(now, now + horizon, zone, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime),
            (int)Math.Ceiling(horizon.TotalDays) + 1);
        var list = new List<CalendarOccurrence>();
        AddAlways(rules, list);
        AddBossCalls(rules, reach, list);
        if (rules.Bellatra == BellatraMode.Hours)
        {
            foreach (var (from, to) in BellatraRules.OpenWindows)
            {
                AddDaily(CalendarEventKind.Bellatra, from, to, reach, list);
            }
        }

        if (rules.CrystalEvent == MonsterCrystals.EventAfternoons)
        {
            AddDaily(CalendarEventKind.CrystalEvent, MonsterCrystals.EventStartHour, HoursPerDay, reach, list);
        }

        AddSieges(rules, reach, list);
        list.Sort(static (a, b) => a.Always != b.Always ? (a.Always ? -1 : 1)
            : a.Start != b.Start ? a.Start.CompareTo(b.Start)
            : a.Kind.CompareTo(b.Kind));
        return list;
    }

    private readonly record struct Reach(DateTimeOffset Now, DateTimeOffset End, TimeZoneInfo Zone, DateOnly Today, int Days)
    {
        public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => end > Now && start < End;
    }

    private static void AddAlways(EventScheduleRules rules, List<CalendarOccurrence> list)
    {
        if (rules.Bellatra == BellatraMode.Always)
        {
            list.Add(Always(CalendarEventKind.Bellatra));
        }

        if (rules.CrystalEvent == MonsterCrystals.EventAllDay)
        {
            list.Add(Always(CalendarEventKind.CrystalEvent));
        }

        if (rules.ExperiencePercent != KillReward.NoEventPercent)
        {
            list.Add(Always(CalendarEventKind.ExperienceEvent));
        }

        if (rules.Drops != DropEvents.None)
        {
            list.Add(Always(CalendarEventKind.DropEvents));
        }

        static CalendarOccurrence Always(CalendarEventKind kind) => new(kind, default, default, CalendarOccurrenceFlags.Always);
    }

    // A call each hour some boss has, at today's minute today and at an unknown minute on later days; a call is a moment,
    // so one that has passed is gone.
    private static void AddBossCalls(EventScheduleRules rules, Reach reach, List<CalendarOccurrence> list)
    {
        Span<ulong> byHour = stackalloc ulong[HoursPerDay];
        for (var index = 0; index < rules.Bosses.Count && index < MaxBosses; index++)
        {
            for (var hour = 0; hour < HoursPerDay; hour++)
            {
                if ((rules.Bosses[index].Hours & 1 << hour) != 0)
                {
                    byHour[hour] |= 1UL << index;
                }
            }
        }

        for (var day = 0; day < reach.Days; day++)
        {
            var date = reach.Today.AddDays(day);
            var known = day == 0 && rules.BossMinuteKnown;
            for (var hour = 0; hour < HoursPerDay; hour++)
            {
                if (byHour[hour] == 0 || !TryUtc(date, hour, known ? rules.BossMinute : 0, reach.Zone, out var start))
                {
                    continue;
                }

                var stillComing = known ? start >= reach.Now : start.AddHours(1) > reach.Now;
                if (stillComing && start < reach.End)
                {
                    list.Add(new CalendarOccurrence(CalendarEventKind.FieldBosses, start, start,
                        known ? CalendarOccurrenceFlags.None : CalendarOccurrenceFlags.MinuteUnknown, byHour[hour]));
                }
            }
        }
    }

    // A window from hour from to hour to (24: the day's end) of each server day.
    private static void AddDaily(CalendarEventKind kind, int from, int to, Reach reach, List<CalendarOccurrence> list)
    {
        for (var day = -1; day < reach.Days; day++)
        {
            var date = reach.Today.AddDays(day);
            if (TryUtc(date, from, 0, reach.Zone, out var start) && TryUtc(date, to, 0, reach.Zone, out var end) && reach.Overlaps(start, end))
            {
                list.Add(new CalendarOccurrence(kind, start, end));
            }
        }
    }

    private static void AddSieges(EventScheduleRules rules, Reach reach, List<CalendarOccurrence> list)
    {
        if (rules.LiveSiege is { } live)
        {
            list.Add(new CalendarOccurrence(CalendarEventKind.CastleSiege, live.Start, live.End));
        }

        if (rules.Castle == CastleMode.Off)
        {
            return;
        }

        var minutes = CastleRules.ScheduledMinutes(rules.Castle);
        var local = TimeZoneInfo.ConvertTime(reach.Now, reach.Zone);
        var first = CastleRules.NextSiege(local.AddMinutes(-minutes), rules.SiegeDay, rules.SiegeHour);
        for (var week = 0; week * 7 < reach.Days; week++)
        {
            var date = DateOnly.FromDateTime(first.DateTime).AddDays(week * 7);
            if (!TryUtc(date, rules.SiegeHour, 0, reach.Zone, out var start))
            {
                continue;
            }

            var end = start.AddMinutes(minutes);
            var replaced = rules.LiveSiege is { } running ? start < running.End && running.Start < end : start <= reach.Now;
            if (!replaced && reach.Overlaps(start, end))
            {
                list.Add(new CalendarOccurrence(CalendarEventKind.CastleSiege, start, end));
            }
        }
    }

    // The UTC moment of a server-local day, hour (24 is the next day's start) and minute; false for a time a
    // daylight-saving change skips.
    private static bool TryUtc(DateOnly date, int hour, int minute, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddHours(hour).AddMinutes(minute);
        if (zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        return true;
    }
}
