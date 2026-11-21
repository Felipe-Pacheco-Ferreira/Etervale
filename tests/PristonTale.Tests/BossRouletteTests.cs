using System.Numerics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.BossRoulette;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Tests;

// The boss roulette (BossRoulette): the entry share, the chances with their floor and cap, the draw and the prize tiers;
// a scheduled boss's death drawn in the world with its notices and its result; the draw kept and its prize mailed; the
// chat filter and the words.
internal static class BossRouletteTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("boss roulette rules: the 1% share, chances by damage with a 5% floor and a 30% cap, the draw and the prize by the boss's level",
            RulesAsync);
        yield return ("a scheduled boss's death draws the roulette among those who fought it and tells everyone; another monster draws nothing", WorldAsync);
        yield return ("a boss roulette's draw is kept and its prize mailed to the winner", StorageAsync);
        yield return ("the boss roulette's notices are hidden by their option alone and worded", WireAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(BossRoulette.Qualifies(100, 10_000) && !BossRoulette.Qualifies(99, 10_000) && !BossRoulette.Qualifies(5, 0), "the 1% share is wrong");
        var two = BossRoulette.Chances([900, 100]);
        Assert.Near(0.9f, two[0]);
        Assert.Near(0.1f, two[1]);
        var supported = BossRoulette.Chances([1000, 0]);
        Assert.Near(BossRoulette.FloorPercent / 100f, supported[1]);
        Assert.True(supported[1] > 0f && MathF.Abs(supported[0] + supported[1] - 1f) < 0.0001f, "a supporter has no chance");
        var crowd = BossRoulette.Chances([9700, 100, 100, 100]);
        Assert.Near(0.3f, crowd[0], 0.001f);
        Assert.Near(0.7f / 3f, crowd[1], 0.001f);
        Assert.True(BossRoulette.Chances([]).Length == 0, "an empty roulette has chances");

        Assert.True(BossRoulette.Draw([0.5f, 0.5f], new ScriptedRng(49)) == 0 && BossRoulette.Draw([0.5f, 0.5f], new ScriptedRng(50)) == 1
            && BossRoulette.Draw([0.5f, 0.5f], new ScriptedRng(100)) == 1, "the draw does not follow the chances");
        Assert.True(BossRoulette.Prize(79, new ScriptedRng(0)) == "BI110" && BossRoulette.Prize(80, new ScriptedRng(1)) == "BI160"
            && BossRoulette.Prize(140, new ScriptedRng(0)) == "BI185", "the prize does not follow the boss's level");
        return Task.CompletedTask;
    }

    private static async Task WorldAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 10, 10, level: 90);
        var bob = await fixture.JoinAsync("Bob", 20, 10, level: 90);
        var cid = await fixture.JoinAsync("Cid", 30, 10, level: 90);
        await fixture.RunAsync(fixture.World.InviteToPartyAsync(ann.Session, cid.Id));
        await fixture.RunAsync(fixture.World.AnswerPartyAsync(cid.Session, ann.Id, true));
        var results = new List<BossRouletteResult>();
        fixture.World.BossRouletteDrawn = results.Add;
        var field = fixture.World.TryGetOpenField(ChatFixture.Field, out var open) ? open : throw new InvalidOperationException("the fixture field is not open");
        var host = (IFieldCombatHost)field;
        var annBody = host.TryGetPlayer(ann.Id, out var foundAnn) ? foundAnn : throw new InvalidOperationException("Ann is not in the field");
        var bobBody = host.TryGetPlayer(bob.Id, out var foundBob) ? foundBob : throw new InvalidOperationException("Bob is not in the field");

        var common = ((ISpawnSink)field).Spawn(SkillScenario.Enemy("Hopy", life: 1000), new Vector3(15, 0, 10), 0, 0);
        ((IFieldPremiumHost)field).PetStrike(annBody, common, 1000, Simulation.Combat.Element.Bionic);
        var boss = ((ISpawnSink)field).Spawn(SkillScenario.Enemy("Valento", life: 10_000, level: 95), new Vector3(15, 0, 10), 0, 0);
        boss.Scheduled = true;
        ((IFieldPremiumHost)field).PetStrike(bobBody, boss, 50, Simulation.Combat.Element.Bionic);
        ((IFieldPremiumHost)field).PetStrike(annBody, boss, 8_000, Simulation.Combat.Element.Bionic);
        // Bob's last blow takes the 1,950 life the boss has left, not its 2,000.
        ((IFieldPremiumHost)field).PetStrike(bobBody, boss, 2_000, Simulation.Combat.Element.Bionic);
        fixture.Tick(1);
        var notices = fixture.World.TakeTickNotices();
        ulong Id(string name) => name == "Ann" ? ann.Id : name == "Bob" ? bob.Id : cid.Id;
        Assert.True(results is [{ BossName: "Valento", Entries.Count: 3, PrizeKey: "BI161" or "BI160" }], "one draw with both fighters and the supporting party member was not made");
        var result = results[0];
        var winner = result.WinnerEntry;
        Assert.True(result.Entries.Single(entry => entry.Name == "Ann") is { Damage: 8_000, Chance: > 0.75f and < 0.77f }
            && result.Entries.Single(entry => entry.Name == "Bob").Damage == 2_000
            && result.Entries.Single(entry => entry.Name == "Cid") is { Damage: 0, Chance: > 0.0499f and < 0.0501f }
            && winner.CharacterId == (long)Id(winner.Name),
            "the entrants' damage, chances or characters are wrong");
        Assert.True(notices.Count(notice => notice.Text.Id == GameTextId.BossRouletteWinner) == 1
            && notices.Any(notice => notice.Text == GameText.Of(GameTextId.BossRouletteWinner, winner.Name, result.PrizeName, "Valento"))
            && notices.Any(notice => notice.To == Id(winner.Name) && notice.Text.Id == GameTextId.BossRouletteWon),
            "the winner was not told to everyone and to itself");
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-roulette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var account = await provider.GetRequiredService<Persistence.Abstractions.Accounts.IAccountRepository>().CreateAsync("ann", "hash");
            var character = await provider.GetRequiredService<ICharacterRepository>().CreateAsync(
                CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), account.Id, "Ann", 0, Contracts.Characters.PlayerClass.Knight, 3));
            var roulette = provider.GetRequiredService<IBossRouletteRepository>();
            var mail = await roulette.RecordAsync(new BossRouletteRecord(3, 7, "Valento", character.Id, "BI161", "[]"), "Roleta", "Parabéns", DateTime.UtcNow);
            var missing = await roulette.RecordAsync(new BossRouletteRecord(3, 7, "Valento", 999_999, "BI161", "[]"), "Roleta", "Parabéns", DateTime.UtcNow);
            Assert.True(mail is > 0 && missing is null, "the prize was not mailed, or a missing character was");
            await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "world.db")}");
            await connection.OpenAsync();
            var check = connection.CreateCommand();
            check.CommandText = "SELECT (SELECT COUNT(*) FROM boss_roulette) * 100 + (SELECT COUNT(*) FROM game_mail_items WHERE item_key = 'BI161' AND quantity = 1)"
                + " * 10 + (SELECT COUNT(*) FROM game_mail WHERE recipient_account_id = sender_account_id)";
            Assert.True((long)(await check.ExecuteScalarAsync())! == 211, "the draws were not both kept with one mailed item");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var winner = new ChatLineInfo(ChatKind.Notice, 0, "", GameText.Of(GameTextId.BossRouletteWinner, "Ann", "Super Aging Stone", "Valento"));
        var own = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.BossRouletteWon, "Super Aging Stone", "Valento"));
        var hiding = new ClientSettings { HideBossRouletteNotices = true };
        Assert.True(ChatLineFilter.Hides(hiding, winner) && !ChatLineFilter.Hides(hiding, own) && !ChatLineFilter.Hides(new ClientSettings(), winner),
            "the roulette's winners were not hidden by the option alone");
        Assert.True(ClientText.Format(winner.Line).Contains("Valento", StringComparison.Ordinal) && ClientText.Format(own.Line).Contains("Super Aging Stone",
            StringComparison.Ordinal), "the roulette's lines are not worded");
        return Task.CompletedTask;
    }
}
