using PristonTale.Application.World;
using PristonTale.Client.Net;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Server.Net;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

internal static class EventCalendarTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("calendar: an open window runs until its last hour and the next one is coming; a window ending now has ended", WindowEdgesAsync);
        yield return ("calendar: boss calls come each boss hour at today's minute, at an unknown minute on later days, never at hour 24",
            BossCallsAsync);
        yield return ("calendar: the coming hours put the boss calls on one card, and the week groups each day's in the viewer's time zone",
            PagesAsync);
        yield return ("calendar: a scheduled siege the castle does not report running is left out, and a running one replaces it", SiegesAsync);
        yield return ("calendar: always-running events lead, crystal afternoons are a daily window, settings off add nothing", AlwaysAsync);
        yield return ("calendar: the world gives its field bosses with their hours, today's minute and the event settings", WorldRulesAsync);
        yield return ("calendar: request and calendar cross the wire whole, in UTC minutes, within one frame; a wrong count is refused",
            WireAsync);
    }

    // Friday 13 November 2026.
    private static readonly DateTimeOffset Day = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.CreateCustomTimeZone("calendar-test-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");

    private static List<CalendarOccurrence> Of(EventScheduleRules rules, DateTimeOffset now, TimeZoneInfo? zone = null) =>
        EventSchedule.Occurrences(rules, now, zone ?? TimeZoneInfo.Utc, EventSchedule.Horizon);

    private static Task WindowEdgesAsync()
    {
        var rules = new EventScheduleRules { Bellatra = BellatraMode.Hours };
        var almost = Of(rules, Day.AddHours(5).AddMinutes(59));
        var running = CalendarPages.Running(almost, Day.AddHours(5).AddMinutes(59));
        Assert.True(running.Count == 1 && running[0].Start == Day && running[0].End == Day.AddHours(6), "the morning window runs until six");
        var coming = CalendarPages.Coming(almost, Day.AddHours(5).AddMinutes(59), CalendarPages.ComingWindow);
        Assert.True(coming.Count == 2 && coming[0].First.Start == Day.AddHours(12) && coming[1].First.Start == Day.AddDays(1),
            "the afternoon and the next morning come within a day");
        var atSix = Of(rules, Day.AddHours(6));
        Assert.True(CalendarPages.Running(atSix, Day.AddHours(6)).Count == 0 && atSix[0].Start == Day.AddHours(12), "a window ending now has ended");
        Assert.True(atSix.Count == 16 && atSix.All(static occurrence => occurrence.Kind == CalendarEventKind.Bellatra),
            "two windows a day over the eight days");
        return Task.CompletedTask;
    }

    private static Task BossCallsAsync()
    {
        var boss = new CalendarBoss("Hopy King", "Forest 1");
        var other = new CalendarBoss("Bargon", "Ruins");
        var rules = new EventScheduleRules
        {
            Bosses = [new ScheduledBoss(boss, EventSchedule.HourMask([1, 2, 24])), new ScheduledBoss(other, EventSchedule.HourMask([2]))],
            BossMinute = 30
        };
        var now = Day.AddHours(1).AddMinutes(30);
        var calls = Of(rules, now);
        Assert.True(calls[0] == new CalendarOccurrence(CalendarEventKind.FieldBosses, now, now, Bosses: 1)
            && calls[1] == new CalendarOccurrence(CalendarEventKind.FieldBosses, now.AddHours(1), now.AddHours(1), Bosses: 3),
            "today's calls come at today's minute with the bosses of their hour");
        Assert.True(calls[2].Start == Day.AddDays(1).AddHours(1) && calls[2].MinuteUnknown && calls[2].End == calls[2].Start,
            "tomorrow's call is its hour, at a minute drawn on the day");
        Assert.True(calls.Count == 17 && calls.All(static call => call.Start.Hour is 1 or 2), "two calls a day over the span, none at hour 24");
        Assert.True(!Of(rules, now.AddSeconds(1)).Any(call => call.Start == now), "a call that has passed is gone");
        return Task.CompletedTask;
    }

    private static Task PagesAsync()
    {
        var rules = new EventScheduleRules
        {
            Bosses = [new ScheduledBoss(new CalendarBoss("Hopy King", "Forest 1"), EventSchedule.HourMask([10, 20]))],
            BossMinute = 15,
            Bellatra = BellatraMode.Hours
        };
        var now = Day.AddHours(9);
        var calendar = Of(rules, now);
        var coming = CalendarPages.Coming(calendar, now, CalendarPages.ComingWindow);
        var calls = coming.Single(static entry => entry.Kind == CalendarEventKind.FieldBosses);
        Assert.True(calls.Occurrences.Count == 2 && calls.First.Start == now.AddHours(1).AddMinutes(15) && coming[0] == calls,
            "the coming boss calls share one card, placed at the first");
        var week = CalendarPages.Days(calendar, now, Brasilia, CalendarPages.WeekDays);
        Assert.True(week.Count == CalendarPages.WeekDays && week[0].Day == new DateOnly(2026, 11, 14), "the week starts tomorrow in the viewer's zone");
        var saturday = week[0].Entries;
        Assert.True(saturday.Count(static entry => entry.Kind == CalendarEventKind.FieldBosses) == 1
            && saturday.Single(static entry => entry.Kind == CalendarEventKind.FieldBosses).Occurrences.Count == 2
            && saturday.Count(static entry => entry.Kind == CalendarEventKind.Bellatra) == 2,
            "a day holds its boss calls on one card and each Bellatra window on its own");
        Assert.True(saturday.All(static entry => entry.Occurrences.All(occurrence =>
            CalendarPages.LocalDay(occurrence.Start, Brasilia) == new DateOnly(2026, 11, 14))), "a day holds what starts on it in the viewer's zone");
        return Task.CompletedTask;
    }

    private static Task SiegesAsync()
    {
        var sunday = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);
        var rules = new EventScheduleRules { Castle = CastleMode.Weekly, SiegeDay = 0, SiegeHour = 20 };
        var before = Of(rules, Day);
        Assert.True(before.Count == 1 && before[0].Start == sunday && before[0].End == sunday.AddMinutes(CastleRules.SiegeMinutes), "the week's siege");
        var during = sunday.AddMinutes(30);
        var ended = Of(rules, during);
        Assert.True(ended.Count == 1 && ended[0].Start == sunday.AddDays(7), "a siege the castle does not report running is left out");
        var live = Of(rules with { LiveSiege = new CalendarWindow(sunday.AddMinutes(10), sunday.AddMinutes(130)) }, during);
        Assert.True(live.Count == 2 && live[0].RunningAt(during) && live[0].Start == sunday.AddMinutes(10) && live[1].Start == sunday.AddDays(7),
            "the running siege replaces the scheduled one");
        Assert.True(Of(rules with { Castle = CastleMode.Off }, Day).Count == 0, "no sieges without the castle");
        return Task.CompletedTask;
    }

    private static Task AlwaysAsync()
    {
        var rules = new EventScheduleRules
        {
            Bellatra = BellatraMode.Always,
            CrystalEvent = Simulation.Items.Usable.MonsterCrystals.EventAfternoons,
            ExperiencePercent = 300,
            Drops = new ItemEventRates { Mandoo = true, Puzzle = 5 }.Running
        };
        var now = Day.AddHours(13);
        var calendar = Of(rules, now);
        Assert.True(calendar.Take(3).All(static occurrence => occurrence.Always)
            && calendar.Take(3).Select(static occurrence => occurrence.Kind).SequenceEqual(
                [CalendarEventKind.Bellatra, CalendarEventKind.ExperienceEvent, CalendarEventKind.DropEvents]),
            "the always-running events lead");
        Assert.True(calendar[3].Kind == CalendarEventKind.CrystalEvent && calendar[3].Start == Day.AddHours(12) && calendar[3].End == Day.AddDays(1)
            && calendar[3].RunningAt(now), "the crystal event runs each afternoon to the day's end");
        Assert.True(rules.Drops == (DropEvents.Mandoo | DropEvents.Puzzle) && new ItemEventRates().Running == DropEvents.None, "the drop events in force");
        Assert.True(Of(new EventScheduleRules(), now).Count == 0, "settings off add nothing");
        return Task.CompletedTask;
    }

    private static Task WorldRulesAsync()
    {
        var king = new MonsterDefinition(7, "Hopy King", MonsterFaction.Enemy, MonsterClass.Boss, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "");
        var empty = WorldData.Empty;
        var field = RicartenField.Definition.FieldCode;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, new MonsterCatalog([king]), empty.Npcs, new Dictionary<int, FieldSpawnData>
            {
                [field] = FieldSpawnData.Empty with { BossSpawns = [new BossSpawn(7, 0, 0, [3, 24]), new BossSpawn(99, 0, 0, [4]), new BossSpawn(7, 0, 0, [24])] }
            }, empty.Motions)
        };
        var world = new WorldServer(holder, new WorldCatalog(), bossRng: new ScriptedRng(17),
            options: new WorldServerOptions { CrystalEvent = 1, ExperienceEventPercent = 0, ItemEvents = new ItemEventRates { GiftBox = 3 } });
        var rules = world.CalendarRules("pt-BR");
        Assert.True(rules.Bosses.Count == 1 && rules.Bosses[0] == new ScheduledBoss(new CalendarBoss("Hopy King", RicartenField.Definition.Name), 1 << 3),
            "a known boss with a real hour, named with its field");
        Assert.True(rules.BossMinute == 17 && rules.CrystalEvent == 1 && rules.ExperiencePercent == KillReward.NoEventPercent
            && rules.Drops == DropEvents.GiftBox && rules.Bellatra == BellatraOptions.Default.Mode, "today's minute and the event settings");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        Assert.True(EventCalendarPacket.MaxSize <= FrameCodec.MaxFrameSize, "the largest calendar fits one frame");
        var request = new EventCalendarRequestPacket(Guid.NewGuid());
        var requestBytes = new byte[EventCalendarRequestPacket.Size];
        Assert.True(request.Write(requestBytes) == requestBytes.Length && EventCalendarRequestPacket.Read(requestBytes) == request, "the request");

        var live = new CalendarWindow(Day.AddHours(20).AddSeconds(20), Day.AddHours(22).AddSeconds(20));
        var rules = new EventScheduleRules
        {
            Bosses = [new ScheduledBoss(new CalendarBoss("Hopy King", "Floresta 1"), EventSchedule.HourMask(Enumerable.Range(0, 24)))],
            BossMinute = 30,
            Bellatra = BellatraMode.Hours,
            Castle = CastleMode.Weekly,
            LiveSiege = live,
            CrystalEvent = Simulation.Items.Usable.MonsterCrystals.EventAfternoons,
            ExperiencePercent = 500,
            Drops = DropEvents.Watermelon
        };
        var now = Day.AddHours(20).AddMinutes(40);
        var sent = EventSchedule.Calendar(rules, now, Brasilia, EventSchedule.Horizon);
        var operation = Guid.NewGuid();
        var buffer = new byte[EventCalendarPacket.MaxSize];
        var length = ClientSession.ToPacket(operation, sent).Write(buffer);
        var packet = EventCalendarPacket.Read(buffer.AsSpan(0, length));
        var received = GameServerConnection.ToCalendar(packet);
        Assert.True(packet.OperationId == operation && received.Bosses.SequenceEqual(sent.Bosses) && received.ExperiencePercent == 500
            && received.Drops == DropEvents.Watermelon, "the bosses and settings");
        Assert.True(sent.Occurrences.Count > 200 && received.Occurrences.Count == sent.Occurrences.Count, "every occurrence of the span");
        var siege = received.Occurrences.Single(static occurrence => occurrence.Kind == CalendarEventKind.CastleSiege && occurrence.Start < Day.AddDays(1));
        Assert.True(siege.Start == Day.AddHours(20) && siege.End == Day.AddHours(22).AddMinutes(1), "a time between minutes widens to whole minutes");
        Assert.True(received.Occurrences.Where(static occurrence => occurrence.Kind != CalendarEventKind.CastleSiege)
                .SequenceEqual(sent.Occurrences.Where(static occurrence => occurrence.Kind != CalendarEventKind.CastleSiege)),
            "whole-minute occurrences and the always-running ones come back as sent");

        var wrong = buffer.AsSpan(0, length).ToArray();
        var countAt = length - received.Occurrences.Count * EventCalendarWire.OccurrenceSize - 4;
        BitConverter.TryWriteBytes(wrong.AsSpan(countAt), received.Occurrences.Count + 1);
        Assert.Throws<InvalidDataException>(() => EventCalendarPacket.Read(wrong), "a count that disagrees with the length");
        return Task.CompletedTask;
    }
}
