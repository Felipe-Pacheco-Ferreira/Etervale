using System.Globalization;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Web.Services;

// The site's words for game data and its formats of numbers and times, in Brazilian Portuguese.
public static class SiteText
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly IReadOnlyList<(DropEvents Drop, string Name)> DropNames =
    [
        (DropEvents.Mandoo, "Bolinhos de arroz"),
        (DropEvents.ValentineDay, "Chocolates do Dia dos Namorados"),
        (DropEvents.Watermelon, "Melancias"),
        (DropEvents.Valentine, "Presentes dos namorados"),
        (DropEvents.CandyDays, "Doces"),
        (DropEvents.WhiteDay, "Chocolates do White Day"),
        (DropEvents.Puzzle, "Peças de quebra-cabeça"),
        (DropEvents.GiftBox, "Caixas de presente")
    ];

    public static string Title(CalendarEventKind kind) => kind switch
    {
        CalendarEventKind.FieldBosses => "Chefes de campo",
        CalendarEventKind.Bellatra => "Bellatra",
        CalendarEventKind.CastleSiege => "Cerco ao Bless Castle",
        CalendarEventKind.CrystalEvent => "Evento dos cristais",
        CalendarEventKind.ExperienceEvent => "Evento de experiência",
        CalendarEventKind.DropEvents => "Drops de evento",
        _ => "Evento"
    };

    public static string Describe(CalendarEventKind kind) => kind switch
    {
        CalendarEventKind.FieldBosses => "Os chefes nascem no mapa de cada um, com seus guardas.",
        CalendarEventKind.Bellatra => $"Sobrevivência em equipe contra ondas de monstros, a partir do nível {BellatraRules.MinLevel}.",
        CalendarEventKind.CastleSiege =>
            $"Os clãs disputam o castelo por até {TimeSpan.FromMinutes(CastleRules.SiegeMinutes).TotalHours} horas; quando a Torre de Valhala cai, o clã que mais a feriu fica com ele.",
        CalendarEventKind.CrystalEvent => "Os cristais de monstro chamam seus monstros de evento.",
        CalendarEventKind.ExperienceEvent => "Experiência extra em todos os mapas.",
        CalendarEventKind.DropEvents => "Os monstros deixam itens de evento.",
        _ => ""
    };

    // The CSS modifier of an event's card.
    public static string Tone(CalendarEventKind kind) => kind switch
    {
        CalendarEventKind.FieldBosses => "boss",
        CalendarEventKind.CastleSiege => "siege",
        CalendarEventKind.Bellatra => "bellatra",
        _ => "event"
    };

    public static string BossMinuteRange => $":{BossSpawnSchedule.FirstMinute:00} e :{BossSpawnSchedule.LastMinute:00}";

    public static string Number(long value) => value.ToString("N0", Culture);

    public static string Day(DateOnly day)
    {
        var text = day.ToString("dddd, d 'de' MMMM", Culture);
        return char.ToUpper(text[0], Culture) + text[1..];
    }

    public static string Date(DateOnly day) => day.ToString("d 'de' MMMM 'de' yyyy", Culture);

    public static string Time(DateTimeOffset moment, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(moment, zone).ToString("HH:mm", Culture);

    // A boss call whose minute is drawn on the day shows its hour only.
    public static string Start(CalendarOccurrence occurrence, TimeZoneInfo zone) => occurrence.MinuteUnknown
        ? TimeZoneInfo.ConvertTime(occurrence.Start, zone).ToString("HH", Culture) + "h"
        : Time(occurrence.Start, zone);

    public static string Window(CalendarOccurrence occurrence, TimeZoneInfo zone) => occurrence.Always ? "O tempo todo"
        : occurrence.End > occurrence.Start ? $"{Time(occurrence.Start, zone)} – {Time(occurrence.End, zone)}"
        : Start(occurrence, zone);

    // The zone's offset as the pages name it, such as UTC−3.
    public static string ZoneName(TimeZoneInfo zone, DateTimeOffset now)
    {
        var offset = zone.GetUtcOffset(now);
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        var hours = Math.Abs(offset.Hours);
        return offset == TimeSpan.Zero ? "UTC"
            : offset.Minutes == 0 ? $"UTC{sign}{hours}" : $"UTC{sign}{hours}:{Math.Abs(offset.Minutes):00}";
    }

    public static string Since(DateTimeOffset since, DateTimeOffset now)
    {
        var span = now - since;
        return span.TotalMinutes < 1 ? "agora mesmo"
            : span.TotalHours < 1 ? $"há {(int)span.TotalMinutes} min"
            : span.TotalDays < 1 ? $"há {(int)span.TotalHours} h"
            : $"há {(int)span.TotalDays} dias";
    }

    // The level medallion's stone, climbing with the level against the highest one as on the game's HUD: amber, emerald,
    // sapphire, crimson, and the highest level burns.
    public static string MedalTone(int level)
    {
        var highest = ExperienceTable.MaximumLevel;
        var share = level / (double)highest;
        return level >= highest ? "burning" : share >= .75 ? "crimson" : share >= .5 ? "sapphire" : share >= .25 ? "emerald" : "amber";
    }

    // How far the experience is from the level's start to the next level's, 0 to 1.
    public static double LevelProgress(int level, long experience)
    {
        var highest = ExperienceTable.MaximumLevel;
        if (level >= highest)
        {
            return 1;
        }

        var floor = ExperienceTable.ForLevel(Math.Clamp(level, 1, highest));
        var next = ExperienceTable.ForLevel(Math.Clamp(level + 1, 1, highest));
        var have = (ulong)Math.Max(0, experience);
        return next <= floor || have <= floor ? 0 : Math.Min(1, (have - floor) / (double)(next - floor));
    }

    public static string Percent(double share) => share.ToString("P0", Culture);

    public static string Size(long bytes) => bytes >= Gigabyte ? (bytes / (double)Gigabyte).ToString("N2", Culture) + " GB"
        : bytes >= Megabyte ? (bytes / (double)Megabyte).ToString("N1", Culture) + " MB"
        : (bytes / 1024.0).ToString("N0", Culture) + " KB";

    private const long Megabyte = 1024 * 1024;
    private const long Gigabyte = 1024 * Megabyte;
}
