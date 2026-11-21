using System.Globalization;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Titles;

namespace PristonTale.Application.World;

// A position title's holder: the title, the character and its name.
public readonly record struct TitleHolder(TitleCode Title, long CharacterId, string Name);

// What the owner's status window shows of its titles: the one shown and those it owns (TitleRules.Bit each: the
// permanent ones earned and the positions it holds).
public sealed record TitleView(TitleCode Shown, uint Owned);

// Titles (TitleRules). Permanent titles are earned on the tick from the character's own book; position titles pass on the
// tick to the holder the storage service finds (AwardTitleAsync) or the Battle Royale names, and TitlePassed hands each
// change to storage. A shown position the character no longer holds is dropped; every player shows its title over its
// name (PlayerAppearanceView.Title). Legendary titles are told to every player, the others to their owner.
public sealed partial class WorldServer
{
    private readonly TitleHolder?[] _titleHolders = new TitleHolder?[TitleRules.Count];
    private long _titleHoldersRevision;

    // Until the stored holders arrive nobody is known to hold a position, so no shown position is dropped.
    private bool _titleHoldersLoaded;

    public Action<TitleHolder>? TitlePassed { get; set; }

    // The holders storage kept, at start: a title the world already handed meanwhile keeps its new holder.
    public Task LoadTitleHoldersAsync(IReadOnlyList<TitleHolder> holders) =>
        EnqueueAsync(NoOwner, () =>
        {
            foreach (var holder in holders)
            {
                if (TitleRules.IsPosition(holder.Title) && _titleHolders[(int)holder.Title] is null)
                {
                    _titleHolders[(int)holder.Title] = holder;
                }
            }

            _titleHoldersLoaded = true;
            _titleHoldersRevision++;
            return true;
        });

    public Task AwardTitleAsync(TitleHolder holder) => EnqueueAsync(NoOwner, () => PassTitle(holder));

    public Task<bool> ChooseTitleAsync(PlayerSession session, TitleCode code) =>
        EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection))
            {
                return false;
            }

            var book = OwnCharacter(connection).Titles;
            if (!book.Choose(code, Holds(connection, code)))
            {
                return false;
            }

            connection.Player.Title = book.Shown;
            return true;
        });

    public TitleHolder? HolderOf(TitleCode code) => TitleRules.IsPosition(code) ? _titleHolders[(int)code] : null;

    // Tick only: the position passes to another character, told to every player.
    private bool PassTitle(TitleHolder holder)
    {
        if (!TitleRules.IsPosition(holder.Title) || holder.CharacterId <= 0)
        {
            return false;
        }

        var previous = _titleHolders[(int)holder.Title];
        if (previous?.CharacterId == holder.CharacterId)
        {
            return false;
        }

        _titleHolders[(int)holder.Title] = holder;
        _titleHoldersRevision++;
        var title = TitleNumber(holder.Title);
        Announce(previous is { } before
            ? GameText.Of(GameTextId.TitleLost, before.Name, title, holder.Name)
            : GameText.Of(GameTextId.TitleGained, holder.Name, title));
        foreach (var connection in _connections.Values)
        {
            var id = CharacterIdOf(connection.Session);
            if (id == holder.CharacterId)
            {
                Notify(connection.Player.EntityId, ChatKind.System, GameText.Of(GameTextId.TitleEarned, title));
            }
            else if (previous is { } lost && id == lost.CharacterId)
            {
                Notify(connection.Player.EntityId, ChatKind.System, GameText.Of(GameTextId.TitleTaken, title, holder.Name));
            }
        }

        TitlePassed?.Invoke(holder);
        return true;
    }

    // Tick only, after the fields step: the permanent titles each player now earned, a shown position it lost, and the
    // titles its owner sees when they changed.
    private void StepTitles()
    {
        foreach (var connection in _connections.Values)
        {
            var player = connection.Player;
            var character = OwnCharacter(connection);
            var book = character.Titles;
            if (connection.TitleRevision < 0)
            {
                RecordOwnedAging(character);
            }

            var earned = book.Observe(character.Level, (character.Quests.ElementaryLog & ElementaryQuestLog.AdventurerJourney) != 0);
            for (var code = 1; earned != 0 && code < TitleRules.Count; code++)
            {
                if ((earned & TitleRules.Bit((TitleCode)code)) != 0)
                {
                    TellEarned(player, character.Name, (TitleCode)code);
                }
            }

            if (_titleHoldersLoaded && TitleRules.IsPosition(book.Shown) && !Holds(connection, book.Shown))
            {
                book.Choose(TitleCode.None, false);
            }

            player.Title = book.Shown;
            if (book.Revision != connection.TitleRevision || _titleHoldersRevision != connection.TitleHoldersRevision)
            {
                connection.TitleRevision = book.Revision;
                connection.TitleHoldersRevision = _titleHoldersRevision;
                connection.Titles = new TitleView(book.Shown, book.Earned | HeldBy(connection));
            }
        }
    }

    // The owner is always told (with how to show it); every player hears of a legendary one.
    private void TellEarned(WorldPlayer player, string name, TitleCode code)
    {
        if (TitleRules.Announced(code))
        {
            Announce(GameText.Of(GameTextId.TitleGained, name, TitleNumber(code)));
        }

        Notify(player.EntityId, ChatKind.System, GameText.Of(GameTextId.TitleEarned, TitleNumber(code)));
    }

    // Once per entry: an item aged before titles existed, or by a game master, counts too.
    private static void RecordOwnedAging(PlayerCharacter character)
    {
        foreach (var item in character.OwnedItems())
        {
            character.Titles.RecordAging(item.UpgradeLevel);
        }
    }

    // The character whose titles count: the player's own, not an event's stand-in.
    private static PlayerCharacter OwnCharacter(Connection connection) => connection.Player.Home ?? connection.Player.Character;

    private bool Holds(Connection connection, TitleCode code) =>
        TitleRules.IsPosition(code) && CharacterIdOf(connection.Session) is { } id && _titleHolders[(int)code]?.CharacterId == id;

    private uint HeldBy(Connection connection)
    {
        var held = 0u;
        if (CharacterIdOf(connection.Session) is not { } id)
        {
            return held;
        }

        foreach (var holder in _titleHolders)
        {
            if (holder?.CharacterId == id)
            {
                held |= TitleRules.Bit(holder.Value.Title);
            }
        }

        return held;
    }

    // A title travels in a game text as its code; the client words it (Title.<code>).
    private static string TitleNumber(TitleCode code) => ((int)code).ToString(CultureInfo.InvariantCulture);
}
