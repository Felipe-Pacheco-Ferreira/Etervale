using PristonTale.Application.Castle;
using PristonTale.Application.World;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.Calendar;

// The rules an event calendar is built from, out of the world's data and settings: the game server's calendar
// (WorldServer.CalendarRules, EventCalendarQuery) and the website's, which reads the same data from storage.
public static class EventCalendarRules
{
    // The field bosses that have a real hour, by field code and then the field's boss order, named from the catalog given.
    public static List<ScheduledBoss> Bosses(IReadOnlyDictionary<int, FieldSpawnData> fieldSpawns, MonsterCatalog monsters, WorldCatalog fields)
    {
        ArgumentNullException.ThrowIfNull(fieldSpawns);
        ArgumentNullException.ThrowIfNull(monsters);
        ArgumentNullException.ThrowIfNull(fields);
        var bosses = new List<ScheduledBoss>();
        foreach (var (fieldId, spawns) in fieldSpawns.OrderBy(static field => field.Key))
        {
            foreach (var boss in spawns.BossSpawns)
            {
                var hours = EventSchedule.HourMask(boss.SpawnHours);
                if (hours != 0 && monsters.TryGet(boss.BossMonsterCode, out var monster))
                {
                    bosses.Add(new ScheduledBoss(new CalendarBoss(monster.Name, fields.FieldName(fieldId)), hours));
                }
            }
        }

        return bosses;
    }

    // The world's events in force; bossMinute is today's minute of the boss calls, null when it is not known.
    public static EventScheduleRules From(WorldServerOptions options, IReadOnlyList<ScheduledBoss> bosses, int? bossMinute)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new EventScheduleRules
        {
            Bosses = bosses,
            BossMinute = bossMinute ?? 0,
            BossMinuteKnown = bossMinute.HasValue,
            Bellatra = options.Bellatra.Mode,
            CrystalEvent = options.CrystalEvent,
            ExperiencePercent = KillReward.EventPercent(options.ExperienceEventPercent),
            Drops = options.ItemEvents.Running
        };
    }

    // The castle's siege schedule; liveSiege is the siege running now, when the castle reports one.
    public static EventScheduleRules WithCastle(EventScheduleRules rules, CastleOptions options, bool available, CalendarWindow? liveSiege)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(options);
        return rules with
        {
            Castle = available ? options.Mode : CastleMode.Off,
            SiegeDay = options.SiegeDay,
            SiegeHour = options.SiegeHour,
            LiveSiege = liveSiege
        };
    }
}
