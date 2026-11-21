using System.Globalization;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Social;

namespace PristonTale.Application.World;

// The lines a change on the command phase answers (parties, trades), taken by the call that made it, and the notices
// the tick makes, taken after it.
public sealed partial class WorldServer
{
    private readonly List<WorldNotice> _notices = new();

    private void Notify(ulong to, ChatKind kind, GameText text) => _notices.Add(new WorldNotice(to, kind, text));

    // A notice for every player in the chat (a high aging, a level milestone, Bellatra's tickets on sale).
    private void Announce(GameText text) => Notify(WorldNotice.Everyone, ChatKind.Notice, text);

    // After the fields step: a level gain that passed a milestone is told to every player.
    private void AnnounceLevels()
    {
        var notices = _options.Notices;
        foreach (var field in _activeFields)
        {
            foreach (var (name, from, to) in field.LevelGains)
            {
                if (ServerNoticeRules.LevelMilestone(from, to, notices.LevelFrom, notices.LevelEvery) is var milestone and > 0)
                {
                    Announce(GameText.Of(GameTextId.LevelNotice, name, milestone.ToString(CultureInfo.InvariantCulture)));
                }
            }

            field.LevelGains.Clear();
        }
    }

    // An item a monster dropped that a party member picked up is told to the other members, by its own text when rare.
    private void TellPartyLoot(PlayerSession session, ItemInstance? item)
    {
        if (item is null || !TryGetOwnConnection(session, out var picker) || picker.Player.Party is not { } party)
        {
            return;
        }

        var text = GameText.Of(item.Definition.IsRare ? GameTextId.PartyRareLoot : GameTextId.PartyLoot, picker.Player.Character.Name, item.Definition.Name);
        foreach (var member in party.Members)
        {
            if (member != picker.Player.EntityId)
            {
                Notify(member, ChatKind.Party, text);
            }
        }
    }

    // A reply to the player who acted.
    private void Reply(WorldPlayer player, GameText text) => Notify(player.EntityId, ChatKind.Server, text);

    // A request that answers only lines: the action runs for a player still playing.
    private WorldNotice[] NoticeAction(PlayerSession session, Action<Connection> action)
    {
        if (TryGetPlaying(session, out var connection))
        {
            action(connection);
        }

        return TakeNotices();
    }

    // The tick's caller, after Tick: the lines no request took (a change of place ends a trade).
    public WorldNotice[] TakeTickNotices() => TakeNotices();

    private WorldNotice[] TakeNotices()
    {
        if (_notices.Count == 0)
        {
            return [];
        }

        var notices = _notices.ToArray();
        _notices.Clear();
        return notices;
    }
}
