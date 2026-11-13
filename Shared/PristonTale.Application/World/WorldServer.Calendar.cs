using PristonTale.Application.Calendar;
using PristonTale.Simulation.World.Calendar;

namespace PristonTale.Application.World;

// What the event calendar reads of the world: the field bosses with their hours, today's boss minute and the event
// settings in force. Reads immutable catalogs and volatile settings only, so any thread may call it.
public sealed partial class WorldServer
{
    // The server's time zone, whose local hours the bosses, Bellatra and the crystal event follow.
    public TimeZoneInfo LocalTimeZone => _clock.LocalTimeZone;

    public DateTimeOffset UtcNow => _clock.GetUtcNow();

    // Boss names in the player's language, by field code and then the field's boss order.
    public EventScheduleRules CalendarRules(string language)
    {
        var data = _worldData.Current;
        var monsters = _localizedData.Get(language, data).Monsters;
        return EventCalendarRules.From(Options, EventCalendarRules.Bosses(data.FieldSpawns, monsters, _catalog), BossMinute);
    }
}
