using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Loot;

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
        var bosses = new List<ScheduledBoss>();
        foreach (var (fieldId, spawns) in data.FieldSpawns.OrderBy(static field => field.Key))
        {
            foreach (var boss in spawns.BossSpawns)
            {
                var hours = EventSchedule.HourMask(boss.SpawnHours);
                if (hours != 0 && monsters.TryGet(boss.BossMonsterCode, out var monster))
                {
                    bosses.Add(new ScheduledBoss(new CalendarBoss(monster.Name, FieldName(fieldId)), hours));
                }
            }
        }

        var options = Options;
        return new EventScheduleRules
        {
            Bosses = bosses,
            BossMinute = BossMinute,
            Bellatra = options.Bellatra.Mode,
            CrystalEvent = options.CrystalEvent,
            ExperiencePercent = KillReward.EventPercent(options.ExperienceEventPercent),
            Drops = options.ItemEvents.Running
        };
    }
}
