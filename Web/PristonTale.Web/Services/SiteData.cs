using Microsoft.Extensions.Caching.Memory;
using PristonTale.Application.Calendar;
using PristonTale.Application.Settings;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Web.Services;

// What the public pages read from storage, each kept for a short while so a busy page does not reach storage per request.
public sealed class SiteData(IRankingRepository rankings, IServerSettingsRepository settings, IWorldDataRepository worldData, WorldCatalog fields,
    IMemoryCache cache, TimeProvider time, ILogger<SiteData> logger)
{
    public const int RankingSize = 50;
    private static readonly TimeSpan ShortLife = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BossLife = TimeSpan.FromHours(1);

    public Task<WorldTotals> TotalsAsync(CancellationToken ct) =>
        CachedAsync("totals", ShortLife, () => rankings.TotalsAsync(ct));

    public Task<IReadOnlyList<RankedCharacter>> TopAsync(PlayerClass? playerClass, CancellationToken ct) =>
        CachedAsync("top:" + playerClass, ShortLife, () => rankings.TopAsync(playerClass is { } job ? (int)job : null, RankingSize, ct));

    // The settings table as the game server reads it; values it refuses keep their defaults, as on the server.
    public Task<ServerSettings> SettingsAsync(CancellationToken ct) => CachedAsync("settings", ShortLife, async () =>
    {
        var problems = new List<string>();
        var read = ServerSettingCatalog.Read(await settings.LoadAsync(ct), null, problems);
        foreach (var problem in problems)
        {
            logger.LogWarning("Server setting refused: {Problem}", problem);
        }

        return read;
    });

    // The event calendar from the settings table and the bosses' hours; today's boss minute is drawn by the game server
    // and not stored, so today's calls show their hour only. The site runs in the game server's time zone.
    public Task<SiteCalendar> CalendarAsync(CancellationToken ct) => CachedAsync("calendar", ShortLife, async () =>
    {
        var server = await SettingsAsync(ct);
        var bosses = await CachedAsync("bosses", BossLife, async () =>
        {
            var monsters = new MonsterCatalog(WorldDataFactory.ToMonsterDefinitions(await worldData.GetAllMonstersAsync(CatalogLanguages.BrazilianPortuguese, ct)));
            var spawns = WorldDataFactory.ToFieldSpawnData(await worldData.GetAllFieldSpawnsAsync(ct));
            return (IReadOnlyList<ScheduledBoss>)EventCalendarRules.Bosses(spawns, monsters, fields);
        });
        var rules = EventCalendarRules.WithCastle(EventCalendarRules.From(server.World, bosses, null), server.Castle, available: true, liveSiege: null);
        var now = time.GetUtcNow();
        var zone = time.LocalTimeZone;
        return SiteCalendar.Build(EventSchedule.Calendar(rules, now, zone, EventSchedule.Horizon), bosses, now, zone);
    });

    private async Task<T> CachedAsync<T>(string key, TimeSpan life, Func<Task<T>> read)
    {
        if (cache.TryGetValue(key, out T? value) && value is not null)
        {
            return value;
        }

        value = await read();
        cache.Set(key, value, life);
        return value;
    }
}
