using PristonTale.Application.Castle;
using PristonTale.Application.World;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World.Calendar;

namespace PristonTale.Application.Calendar;

// The event calendar a player opens: the world's events and bosses (WorldServer.CalendarRules) with the castle's siege
// schedule and the siege running now, over EventSchedule.Horizon from the server's clock. Read-only; any thread.
public static class EventCalendarQuery
{
    public static EventCalendar For(WorldServer world, CastleAppService castle, string language)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(castle);
        var view = castle.View();
        var liveSiege = view.Siege == SiegeState.Running
            ? new CalendarWindow(DateTimeOffset.FromUnixTimeSeconds(view.SiegeStartedAt), DateTimeOffset.FromUnixTimeSeconds(view.SiegeEndsAt))
            : (CalendarWindow?)null;
        var rules = EventCalendarRules.WithCastle(world.CalendarRules(language), castle.Options, castle.Available, liveSiege);
        return EventSchedule.Calendar(rules, world.UtcNow, world.LocalTimeZone, EventSchedule.Horizon);
    }
}
