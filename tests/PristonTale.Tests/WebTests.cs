using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Web.Hosting;
using PristonTale.Web.Services;

namespace PristonTale.Tests;

internal static class WebTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("website: every response forbids scripts, framing and sniffing", HeadersAsync);
        yield return ("website: settings out of range stop the start", SettingsAsync);
        yield return ("website: limits count an IPv4 address or an IPv6 /64 network", LimitKeysAsync);
        yield return ("website: a session ends when its account is blocked, gets a new password or is another one", SessionAsync);
        yield return ("website: class titles read the client's Portuguese texts and fall back to the English title", ClassTitlesAsync);
        yield return ("website: boss calls are kept apart from the other events, with each boss's hours", CalendarAsync);
        yield return ("website: level medallions climb with the level and experience fills toward the next level", LevelsAsync);
        yield return ("website: rankings leave out game masters and blocked accounts and order by level then experience; names are taken in any case",
            RankingsAsync);
    }

    private static Task HeadersAsync()
    {
        IHeaderDictionary headers = new HeaderDictionary();
        SecurityHeaders.Apply(headers);
        var policy = headers.ContentSecurityPolicy.ToString();
        Assert.True(policy.Contains("default-src 'none'", StringComparison.Ordinal) && !policy.Contains("script-src", StringComparison.Ordinal)
            && policy.Contains("frame-ancestors 'none'", StringComparison.Ordinal) && policy.Contains("form-action 'self'", StringComparison.Ordinal),
            "the content security policy");
        Assert.True(headers.XFrameOptions == "DENY" && headers.XContentTypeOptions == "nosniff" && headers["Referrer-Policy"] == "same-origin",
            "the frame, sniffing and referrer headers");
        return Task.CompletedTask;
    }

    private static Task LimitKeysAsync()
    {
        static string Key(string address) => WebHosting.LimitKey(System.Net.IPAddress.Parse(address)).ToString();

        Assert.True(Key("2001:db8:1:2:aaaa::1") == Key("2001:db8:1:2:bbbb::9") && Key("2001:db8:1:2::1") != Key("2001:db8:1:3::1"),
            "one /64 is one key, the next /64 another");
        Assert.True(Key("::ffff:10.0.0.7") == "10.0.0.7" && Key("10.0.0.7") != Key("10.0.0.8") && WebHosting.LimitKey(null).Equals(System.Net.IPAddress.None),
            "IPv4 addresses, mapped or not, and no address");
        return Task.CompletedTask;
    }

    private static Task SettingsAsync()
    {
        static WebSettings Read(params (string Key, string Value)[] values) => WebSettings.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build());

        var defaults = Read();
        Assert.True(defaults.Database.IsSqlite && defaults.GameServer.Port == 10009 && defaults.Security.LoginFailuresPerAccount == 5,
            "the defaults");
        Assert.Throws<InvalidOperationException>(() => Read(("Database:Provider", "Oracle")), "refused: Database:Provider = Oracle");
        Assert.Throws<InvalidOperationException>(() => Read(("Database:Provider", "Postgres")), "refused: Database:Provider = Postgres");
        Assert.Throws<InvalidOperationException>(() => Read(("Security:FormPostsPerMinute", "0")), "refused: Security:FormPostsPerMinute = 0");
        Assert.Throws<InvalidOperationException>(() => Read(("Site:KnownProxies:0", "proxy.example")), "refused: Site:KnownProxies:0 = proxy.example");
        Assert.Throws<InvalidOperationException>(() => Read(("GameServer:ProbeSeconds", "1")), "refused: GameServer:ProbeSeconds = 1");
        return Task.CompletedTask;
    }

    private static Task SessionAsync()
    {
        var account = new AccountRecord(7, "Hero", PasswordHasher.Hash("segredo123"), false, false);
        var principal = AccountSession.PrincipalOf(account);
        Assert.True(AccountSession.Matches(principal, account) && principal.Identity?.Name == "Hero", "the session of its account");
        Assert.True(!principal.Claims.Any(claim => claim.Value.Contains(account.PasswordHash, StringComparison.Ordinal)), "the cookie holds no hash");
        Assert.True(!AccountSession.Matches(principal, account with { IsBlocked = true }), "a blocked account");
        Assert.True(!AccountSession.Matches(principal, account with { PasswordHash = PasswordHasher.Hash("outrasenha") }), "a new password");
        Assert.True(!AccountSession.Matches(principal, account with { Id = 8 }) && !AccountSession.Matches(principal, null), "another account or none");
        return Task.CompletedTask;
    }

    private static Task ClassTitlesAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"priston-titles-{Guid.NewGuid():N}.resx");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Class.Archer.0" xml:space="preserve"><value>Arqueira</value></data>
              <data name="Skill.01010101.Name" xml:space="preserve"><value>Outro texto</value></data>
            </root>
            """);
        try
        {
            var titles = ClassTitles.Load(path);
            Assert.True(titles.Of(PlayerClass.Archer, 0) == "Arqueira", "a Portuguese title");
            Assert.True(titles.Of(PlayerClass.Archer, 99) == ClassProfiles.Of(PlayerClass.Archer).Titles[^1], "a missing tier is the English last title");
            Assert.True(titles.Of(999, 0) == "—", "a job code no class has");
            Assert.True(ClassTitles.FromSlug(ClassTitles.Slug(PlayerClass.MartialArtist)) == PlayerClass.MartialArtist
                && ClassTitles.FromSlug("<script>") is null, "the rankings filter");
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static Task CalendarAsync()
    {
        var now = new DateTimeOffset(2026, 11, 13, 10, 30, 0, TimeSpan.Zero);
        var king = new ScheduledBoss(new CalendarBoss("Hopy King", "Forest 1"), EventSchedule.HourMask([11, 15]));
        var rat = new ScheduledBoss(new CalendarBoss("Rat", "Ruin 1"), EventSchedule.HourMask([3]));
        var rules = new EventScheduleRules { Bosses = [king, rat], BossMinuteKnown = false, Bellatra = Simulation.World.Bellatra.BellatraMode.Hours };
        var calendar = SiteCalendar.Build(EventSchedule.Calendar(rules, now, TimeZoneInfo.Utc, EventSchedule.Horizon), rules.Bosses, now, TimeZoneInfo.Utc);
        Assert.True(calendar.NextCalls.Count == SiteCalendar.CallsShown && calendar.NextCalls[0].Call.Start == now.AddMinutes(30)
            && calendar.NextCalls[0].Bosses.Single().Name == "Hopy King", "the next calls with their bosses");
        Assert.True(calendar.Coming.All(static entry => entry.Kind != CalendarEventKind.FieldBosses)
            && calendar.Days.SelectMany(static day => day.Entries).All(static entry => entry.Kind == CalendarEventKind.Bellatra), "the other events");
        Assert.True(calendar.Bosses.Count == 2 && calendar.Bosses[0].Boss.Name == "Rat" && calendar.Bosses[1].Hours.SequenceEqual([11, 15]),
            "each boss's hours, earliest first");
        return Task.CompletedTask;
    }

    private static Task LevelsAsync()
    {
        var highest = ExperienceTable.MaximumLevel;
        Assert.True(SiteText.MedalTone(1) == "amber" && SiteText.MedalTone(highest / 4) == "emerald" && SiteText.MedalTone(highest / 2) == "sapphire"
            && SiteText.MedalTone(highest * 3 / 4) == "crimson" && SiteText.MedalTone(highest) == "burning", "the medallion stones");
        var floor = (long)ExperienceTable.ForLevel(10);
        var next = (long)ExperienceTable.ForLevel(11);
        Assert.Near(0.5f, (float)SiteText.LevelProgress(10, floor + (next - floor) / 2), 0.01f);
        Assert.True(SiteText.LevelProgress(10, 0) == 0 && SiteText.LevelProgress(highest, 0) == 1, "below the level's start and at the top");
        return Task.CompletedTask;
    }

    private static async Task RankingsAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-web-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var accounts = provider.GetRequiredService<IAccountRepository>();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var rankings = provider.GetRequiredService<IRankingRepository>();
            var classes = new CharacterClassRegistry();
            var player = await accounts.CreateAsync("Player", PasswordHasher.Hash("x"));
            var master = await accounts.CreateAsync("Master", PasswordHasher.Hash("x"));
            var banned = await accounts.CreateAsync("Banned", PasswordHasher.Hash("x"));
            await provider.GetRequiredService<IGameMasterRepository>().SetGmLevelAsync("Master", 3);
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("UPDATE accounts SET is_blocked = 1 WHERE id = @id", new { id = banned.Id });
            }

            async Task<CharacterRecord> Add(AccountRecord account, string name, short slot, PlayerClass playerClass, int level, long exp) =>
                await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, name, slot, playerClass, 3, level: level) with { Exp = exp });

            await Add(player, "Lower", 0, PlayerClass.Knight, 40, 10);
            var top = await Add(player, "Upper", 1, PlayerClass.Fighter, 40, 20);
            await Add(player, "Third", 2, PlayerClass.Knight, 12, 0);
            await Add(master, "Keeper", 0, PlayerClass.Knight, 99, 0);
            await Add(banned, "Cheater", 0, PlayerClass.Knight, 98, 0);
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                var clan = await connection.ExecuteScalarAsync<long>(
                    "INSERT INTO clans (name, name_key, state, created_at, deadline_at) VALUES ('Avalon', 'avalon', 0, 0, 0) RETURNING id");
                await connection.ExecuteAsync("INSERT INTO clan_members (character_id, clan_id, rank, joined_at) VALUES (@character, @clan, 0, 0)",
                    new { character = top.Id, clan });
            }

            var all = await rankings.TopAsync(null, 10);
            Assert.True(all.Select(static row => row.Name).SequenceEqual(["Upper", "Lower", "Third"]), "players only, by level then experience");
            Assert.True(all[0].ClanName == "Avalon" && all[1].ClanName is null && all[0].JobCode == (int)PlayerClass.Fighter, "clans and classes");
            var knights = await rankings.TopAsync((int)PlayerClass.Knight, 1);
            Assert.True(knights.Single().Name == "Lower", "one class, within the limit");
            var totals = await rankings.TotalsAsync();
            Assert.True(totals == new WorldTotals(3, 5), "the totals count every account and character");
            Assert.True(await accounts.IsUserIdTakenAsync("PLAYER") && await accounts.IsUserIdTakenAsync("player") && !await accounts.IsUserIdTakenAsync("Playe"),
                "a name is taken in any case");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
