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
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Abstractions.Titles;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Titles;

namespace PristonTale.Tests;

// Titles (TitleRules): the book's goals and choices, the world earning and passing titles with their notices and the
// title over the name, storage of a character's titles and of the holders, and the wire, the chat filter and the words.
internal static class TitleTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("titles: goals earn permanent titles once, a title is shown only when owned, and a character change keeps the aging reached",
            RulesAsync);
        yield return ("the world tells earned titles, shows the chosen one over the name and passes a position title, dropping it from the one who lost it",
            WorldAsync);
        yield return ("a character's titles and the position holders are stored", StorageAsync);
        yield return ("titles reach the wire, the chat filter and the client's words", WireAsync);
    }

    private static Task RulesAsync()
    {
        var book = new TitleBook();
        var earned = book.Observe(120, false);
        Assert.True(earned == (TitleRules.Bit(TitleCode.Veteran) | TitleRules.Bit(TitleCode.Master)) && book.Observe(120, false) == 0 && !book.Has(TitleCode.Legend),
            "the level titles were not earned once");
        for (var kill = 0; kill < 999; kill++)
        {
            book.CountKill(kill < 99);
        }

        Assert.True(book.Observe(1, false) == 0, "a title was earned before its goal");
        book.CountKill(true);
        book.RecordAging(14);
        Assert.True(book.Observe(1, true) == (TitleRules.Bit(TitleCode.Hunter) | TitleRules.Bit(TitleCode.BossHunter) | TitleRules.Bit(TitleCode.Artisan)
            | TitleRules.Bit(TitleCode.Adventurer)), "the counted titles were not earned at their goals");

        Assert.True(!book.Choose(TitleCode.Legend, false) && !book.Choose(TitleCode.Strongest, false) && !book.Choose((TitleCode)99, true)
            && book.Shown == TitleCode.None, "a title the character does not own was shown");
        Assert.True(book.Choose(TitleCode.Strongest, true) && book.Choose(TitleCode.Master, false) && book.Shown == TitleCode.Master
            && book.Choose(TitleCode.None, false) && book.Shown == TitleCode.None, "an owned title or none could not be shown");
        Assert.Throws<InvalidDataException>(() => book.Restore(new TitleState(TitleRules.Bit(TitleCode.Survivor), TitleCode.None, 0, 0, 0)),
            "a position title was restored as earned");
        Assert.Throws<ArgumentOutOfRangeException>(() => TitleRules.ArenaKing(3), "an arena title was given to a band that does not exist");
        Assert.True(TitleRules.ArenaKing(2) == TitleCode.ArenaKingHigh && TitleRules.Announced(TitleCode.Survivor) && !TitleRules.Announced(TitleCode.Hunter),
            "the arena bands or the announced titles are wrong");

        var character = new CharacterClassRegistry().CreatePlayer(Contracts.Characters.PlayerClass.Fighter, "Ann", 10);
        character.Titles.CountKill(false);
        var candidate = character.CloneForInventoryOperation();
        candidate.Titles.RecordAging(AgingMax);
        character.Titles.CountKill(false);
        character.CommitInventoryOperation(candidate);
        Assert.True(character.Titles is { BestAging: AgingMax, MonsterKills: 2 }, "a character change lost the aging reached or the kills counted meanwhile");
        return Task.CompletedTask;
    }

    private const int AgingMax = Simulation.Items.Aging.AgingRules.MaxLevel;

    private static async Task WorldAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 10, 10, level: 100);
        var bob = await fixture.JoinAsync("Bob", 40, 10);
        fixture.Tick(2);
        var notices = fixture.World.TakeTickNotices();
        Assert.True(notices.Any(notice => notice.To == ann.Id && notice.Text == GameText.Of(GameTextId.TitleEarned, "1"))
            && !notices.Any(notice => notice.Text.Id == GameTextId.TitleGained), "a common title was not told to its owner alone");

        Assert.True(await fixture.RunAsync(fixture.World.ChooseTitleAsync(ann.Session, TitleCode.Veteran))
            && !await fixture.RunAsync(fixture.World.ChooseTitleAsync(bob.Session, TitleCode.Veteran)), "a title was shown by who does not own it");
        fixture.Tick(2);
        var latest = ChatFixture.Latest(ann);
        Assert.True(latest is { Titles: { Shown: TitleCode.Veteran } titles } && (titles.Owned & TitleRules.Bit(TitleCode.Veteran)) != 0
            && ChatFixture.Latest(bob)!.Value.Entities.Single(entity => entity.EntityId == ann.Id).Appearance.Title == (int)TitleCode.Veteran,
            "the chosen title did not reach its owner or the name over the head");

        await fixture.RunAsync(fixture.World.AwardTitleAsync(new TitleHolder(TitleCode.Strongest, (long)ann.Id, "Ann")));
        Assert.True(await fixture.RunAsync(fixture.World.ChooseTitleAsync(ann.Session, TitleCode.Strongest)), "a held position could not be shown");
        await fixture.RunAsync(fixture.World.AwardTitleAsync(new TitleHolder(TitleCode.Strongest, (long)ann.Id, "Ann")));
        await fixture.RunAsync(fixture.World.AwardTitleAsync(new TitleHolder(TitleCode.Strongest, (long)bob.Id, "Bob")));
        await fixture.RunAsync(fixture.World.LoadTitleHoldersAsync([new TitleHolder(TitleCode.Strongest, 999, "Old")]));
        fixture.Tick(2);
        notices = fixture.World.TakeTickNotices();
        var strongest = ((int)TitleCode.Strongest).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(notices.Count(notice => notice.Text == GameText.Of(GameTextId.TitleGained, "Ann", strongest)) == 1
            && notices.Any(notice => notice.Text == GameText.Of(GameTextId.TitleLost, "Ann", strongest, "Bob"))
            && notices.Any(notice => notice.To == bob.Id && notice.Text == GameText.Of(GameTextId.TitleEarned, strongest))
            && notices.Any(notice => notice.To == ann.Id && notice.Text == GameText.Of(GameTextId.TitleTaken, strongest, "Bob"))
            && fixture.World.HolderOf(TitleCode.Strongest) is { Name: "Bob" }, "the position did not pass once to each holder with its notices");
        latest = ChatFixture.Latest(ann);
        Assert.True(latest is { Titles.Shown: TitleCode.None } && (latest.Value.Titles!.Owned & TitleRules.Bit(TitleCode.Strongest)) == 0
            && (ChatFixture.Latest(bob)!.Value.Titles!.Owned & TitleRules.Bit(TitleCode.Strongest)) != 0, "the lost position was still shown or owned");
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-titles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var accounts = provider.GetRequiredService<Persistence.Abstractions.Accounts.IAccountRepository>();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var classes = new CharacterClassRegistry();
            var account = await accounts.CreateAsync("ann", "hash");
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, account.Id, "Ann", 0, Contracts.Characters.PlayerClass.Knight, 3));
            Assert.True(TitleStateCodec.Read(created.Titles) == TitleState.Empty, "a new character did not start with no title");

            var character = classes.CreatePlayer(Contracts.Characters.PlayerClass.Knight, "Ann", 120);
            character.Titles.CountKill(true);
            character.Titles.RecordAging(15);
            character.Titles.Observe(120, true);
            character.Titles.Choose(TitleCode.Master, false);
            var stored = CharacterRecordFactory.FromLiveSession(created, character, 3, default) with { SaveSeq = 1 };
            Assert.True(await characters.UpdateAsync(stored), "the character was not saved");
            var loaded = (await characters.ListByAccountAsync(account.Id)).Single();
            Assert.True(TitleStateCodec.Read(loaded.Titles) == character.Titles.Capture(), "the titles did not round-trip");

            var titles = provider.GetRequiredService<ITitleRepository>();
            await titles.SetHolderAsync(new TitleHolderRecord((int)TitleCode.Survivor, created.Id, "Ann"), DateTime.UtcNow);
            await titles.SetHolderAsync(new TitleHolderRecord((int)TitleCode.Survivor, 42, "Bob"), DateTime.UtcNow);
            await titles.SetHolderAsync(new TitleHolderRecord((int)TitleCode.CastleLord, created.Id, "Ann"), DateTime.UtcNow);
            Assert.True(await titles.HoldersAsync() is [{ Title: (int)TitleCode.Survivor, CharacterId: 42, Name: "Bob" }, { Title: (int)TitleCode.CastleLord, Name: "Ann" }],
                "a position kept two holders or lost one");
            Assert.True(await provider.GetRequiredService<IRankingRepository>().TopAsync(null, 1) is [{ Name: "Ann" } first] && first.Id == created.Id,
                "the ranking did not name its first character's id");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var buffer = new byte[TitlesPacket.Size];
        var titles = new TitlesPacket((byte)TitleCode.Survivor, TitleRules.Bit(TitleCode.Survivor) | TitleRules.Bit(TitleCode.Hunter));
        var choice = new ChooseTitlePacket((byte)TitleCode.Hunter);
        Assert.True(TitlesPacket.Read(buffer.AsSpan(0, titles.Write(buffer))) == titles && ChooseTitlePacket.Read(buffer.AsSpan(0, choice.Write(buffer))) == choice,
            "the titles did not round-trip");
        var info = new TitlesInfo(titles.Shown, titles.Owned);
        Assert.True(info.Owns((int)TitleCode.Hunter) && !info.Owns((int)TitleCode.Legend) && !info.Owns(0) && !info.Owns(40), "the client misreads owned titles");
        var description = new EntityDescriptionPacket(7, 1, Contracts.World.EntityKind.Player, 110, 1, 1f, "Ann", "",
            new DescribedPlayer(Contracts.Characters.PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, false, false, 0, 0) { Title = (int)TitleCode.CastleLord });
        var looks = new byte[EntityDescriptionPacket.MaximumSize];
        Assert.True(EntityDescriptionPacket.Read(looks.AsSpan(0, description.Write(looks))).Player is { Title: (int)TitleCode.CastleLord },
            "the title over the name did not round-trip");

        var gained = new ChatLineInfo(ChatKind.Notice, 0, "", GameText.Of(GameTextId.TitleGained, "Ann", "14"));
        var lost = new ChatLineInfo(ChatKind.Notice, 0, "", GameText.Of(GameTextId.TitleLost, "Ann", "14", "Bob"));
        var own = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.TitleEarned, "14"));
        var hiding = new ClientSettings { HideTitleNotices = true };
        Assert.True(ChatLineFilter.Hides(hiding, gained) && ChatLineFilter.Hides(hiding, lost) && !ChatLineFilter.Hides(hiding, own)
            && !ChatLineFilter.Hides(new ClientSettings(), gained), "the title notices were not hidden by the option alone");
        var survivor = ClientText.TitleName((int)TitleCode.Survivor);
        Assert.True(survivor.Length > 0 && !survivor.StartsWith("Title.", StringComparison.Ordinal) && ClientText.Format(lost.Line).Contains(survivor, StringComparison.Ordinal)
            && ClientText.Format(own.Line).Contains(survivor, StringComparison.Ordinal) && ClientText.TitleName(0).Length == 0, "a title was not worded by its code");
        foreach (var title in TitleRules.All)
        {
            Assert.True(!ClientText.TitleName((int)title.Code).StartsWith("Title.", StringComparison.Ordinal), $"{title.Code} has no words");
        }

        return Task.CompletedTask;
    }
}
