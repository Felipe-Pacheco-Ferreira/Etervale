using System.Globalization;
using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// One player's result of a match, for the week's arena points.
public sealed record ArenaResultEntry(long CharacterId, string Name, ArenaOutcome Outcome);

// A finished match: the server-local week it counts for, its level band and mode, and every member's result.
public sealed record ArenaResult(DateOnly Week, int Band, ArenaMode Mode, IReadOnlyList<ArenaResultEntry> Entries);

// The arena (ArenaRules), on the tick thread. "/arena 1x1", "/arena 3x3" and "/arena sair" (JoinArenaAsync) queue a
// player alone or a party of three led by the player, or take its team out of the queue. While the arena is open
// (WorldServerOptions.Arena), every PairEveryMs the oldest two valid teams of one mode and band start a match: their
// members go to their side's places in the arena's field with their whole life; a blow that would kill takes the target
// out (StepFights, TakeOutOfMatch), as do death and leaving the world; one who leaves the field on its own leaves the
// match where it went. Nobody queues from Bellatra or the quest arena. The match ends when a side has nobody left or at
// MatchMs; every member still in it goes back with its whole life to the field and place it queued from, is told its result, and everyone reads the outcome (ArenaNotice, ArenaDrawNotice). ArenaResultRecorded hands
// the results to the storage of the week's points. The queue empties when the arena closes; opening is told to everyone.
public sealed partial class WorldServer
{
    private static readonly ArenaView QueuedSolo = new(ArenaMode.Solo, true, 0, 0, 0, false);
    private static readonly ArenaView QueuedTrio = new(ArenaMode.Trio, true, 0, 0, 0, false);
    private readonly List<ArenaQueueEntry> _arenaQueue = [];
    private readonly List<ArenaMatch> _matches = [];
    private long _nextMatchId;
    private bool _arenaOpen;
    private float _pairMsLeft;

    // A finished match's results, on the tick thread: the receiver stores them off the tick.
    public Action<ArenaResult>? ArenaResultRecorded { get; set; }

    // Queues the player's team for a mode, or (null) takes it out of the queue.
    public Task<ArenaJoin?> JoinArenaAsync(PlayerSession session, ArenaMode? mode) =>
        EnqueueAsync<ArenaJoin?>(session.EntityId, () => TryGetPlaying(session, out var connection) ? JoinArena(connection.Player, mode) : null);

    private ArenaJoin JoinArena(WorldPlayer player, ArenaMode? mode)
    {
        var answer = mode is { } wanted ? QueueTeam(player, wanted) : LeaveQueue(player);
        if (answer is ArenaJoin.Queued or ArenaJoin.Left)
        {
            return answer;
        }

        Notify(player.EntityId, ChatKind.System, answer switch
        {
            ArenaJoin.Closed => GameText.Of(GameTextId.ArenaClosed, ArenaHours()),
            ArenaJoin.Level => GameText.Of(GameTextId.ArenaLevel, ArenaRules.MinimumLevel),
            ArenaJoin.Party => GameText.Of(GameTextId.ArenaParty, ArenaRules.TeamSize(ArenaMode.Trio)),
            ArenaJoin.NotQueued => GameText.Of(GameTextId.ArenaNotQueued),
            _ => GameText.Of(GameTextId.ArenaBusy)
        });
        return answer;
    }

    private string ArenaHours() =>
        string.Join(", ", Simulation.World.Calendar.EventSchedule.Hours(_options.Arena.Hours).Select(static hour => hour.ToString("00", CultureInfo.InvariantCulture) + ":00"));

    private ArenaJoin QueueTeam(WorldPlayer player, ArenaMode mode)
    {
        if (!_arenaOpen)
        {
            return ArenaJoin.Closed;
        }

        ulong[] members;
        if (mode == ArenaMode.Solo)
        {
            members = [player.EntityId];
        }
        else if (player.Party is { Count: var count } party && count == ArenaRules.TeamSize(mode) && party.LeaderId == player.EntityId)
        {
            members = party.Members.ToArray();
        }
        else
        {
            return ArenaJoin.Party;
        }

        var band = -1;
        var origins = new (int FieldId, Vector3 Position)[members.Length];
        for (var index = 0; index < members.Length; index++)
        {
            if (!_connections.TryGetValue(members[index], out var member) || !FreeForArena(member, null))
            {
                return ArenaJoin.Busy;
            }

            var memberBand = ArenaRules.Band(member.Player.Level);
            if (memberBand < 0)
            {
                return ArenaJoin.Level;
            }

            band = Math.Max(band, memberBand);
            origins[index] = (member.FieldId, member.Player.Position);
        }

        var entry = new ArenaQueueEntry(mode, band, members, origins, UnixNowMs);
        _arenaQueue.Add(entry);
        foreach (var id in members)
        {
            _connections[id].Player.ArenaQueued = entry;
            Notify(id, ChatKind.System, GameText.Of(GameTextId.ArenaQueued, ArenaRules.ModeName(mode)));
        }

        return ArenaJoin.Queued;
    }

    // Free to queue (queued: null) or to start the match it queued for: alive, shown, in no duel, match or Battle Royale (registered or not), and not in a
    // field a return scroll cannot leave (Bellatra, the quest arena), since the match sends it back where it queued.
    private static bool FreeForArena(Connection connection, ArenaQueueEntry? queued)
    {
        var player = connection.Player;
        return !player.IsDead && !player.Hidden && player.Duel is null && player.Arena is null && ReferenceEquals(player.ArenaQueued, queued)
            && player.Royale is null && player.RoyaleEntry is null
            && Array.IndexOf(ItemUses.TravelRules.ReturnBlockedFields, connection.FieldId) < 0;
    }

    // Every member is still in the world and free, and a party's members still share a party of the team's size.
    private bool StillQueued(ArenaQueueEntry entry)
    {
        Party? party = null;
        foreach (var id in entry.Members)
        {
            if (!_connections.TryGetValue(id, out var member) || !FreeForArena(member, entry))
            {
                return false;
            }

            if (entry.Mode != ArenaMode.Solo)
            {
                party ??= member.Player.Party;
                if (party is null || !ReferenceEquals(party, member.Player.Party) || party.Count != ArenaRules.TeamSize(entry.Mode))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private ArenaJoin LeaveQueue(WorldPlayer player)
    {
        if (player.ArenaQueued is not { } entry)
        {
            return ArenaJoin.NotQueued;
        }

        DropFromQueue(entry, GameText.Of(GameTextId.ArenaLeft));
        return ArenaJoin.Left;
    }

    private void DropFromQueue(ArenaQueueEntry entry, GameText told)
    {
        _arenaQueue.Remove(entry);
        foreach (var id in entry.Members)
        {
            if (_connections.TryGetValue(id, out var member) && ReferenceEquals(member.Player.ArenaQueued, entry))
            {
                member.Player.ArenaQueued = null;
                Notify(id, ChatKind.System, told);
            }
        }
    }

    // Tick thread, from StepFights.
    private void StepArena(float dtMs)
    {
        var open = ArenaRules.IsOpen(_options.Arena.Hours, _clock.GetLocalNow());
        if (open != _arenaOpen)
        {
            _arenaOpen = open;
            _pairMsLeft = ArenaRules.PairEveryMs;
            if (open)
            {
                NotifyEveryone(GameText.Of(GameTextId.ArenaOpen, ArenaRules.OpenMinutes));
            }
            else
            {
                for (var index = _arenaQueue.Count - 1; index >= 0; index--)
                {
                    DropFromQueue(_arenaQueue[index], GameText.Of(GameTextId.ArenaQueueClosed));
                }
            }
        }

        if (_arenaOpen)
        {
            _pairMsLeft -= dtMs;
            if (_pairMsLeft <= 0f)
            {
                _pairMsLeft = ArenaRules.PairEveryMs;
                PairTeams();
            }
        }

        for (var index = _matches.Count - 1; index >= 0; index--)
        {
            StepMatch(_matches[index], dtMs);
        }
    }

    private void NotifyEveryone(GameText text)
    {
        foreach (var id in _connections.Keys)
        {
            Notify(id, ChatKind.System, text);
        }
    }

    // Teams whose members are not all still in the world and free leave the queue; then the oldest two of each mode and
    // band start a match, again and again.
    private void PairTeams()
    {
        for (var index = _arenaQueue.Count - 1; index >= 0; index--)
        {
            var entry = _arenaQueue[index];
            if (!StillQueued(entry))
            {
                DropFromQueue(entry, GameText.Of(GameTextId.ArenaLeft));
            }
        }

        for (var first = 0; first < _arenaQueue.Count; first++)
        {
            for (var second = first + 1; second < _arenaQueue.Count; second++)
            {
                if (_arenaQueue[first].Mode == _arenaQueue[second].Mode && _arenaQueue[first].Band == _arenaQueue[second].Band)
                {
                    var (a, b) = (_arenaQueue[first], _arenaQueue[second]);
                    _arenaQueue.RemoveAt(second);
                    _arenaQueue.RemoveAt(first);
                    StartMatch(a, b);
                    first--;
                    break;
                }
            }
        }
    }

    private void StartMatch(ArenaQueueEntry first, ArenaQueueEntry second)
    {
        ArenaQueueEntry[] sides = [first, second];
        var connections = new Connection[2][];
        var names = new string[2][];
        var characterIds = new long[2][];
        for (var side = 0; side < 2; side++)
        {
            var members = sides[side].Members;
            connections[side] = new Connection[members.Length];
            names[side] = new string[members.Length];
            characterIds[side] = new long[members.Length];
            for (var member = 0; member < members.Length; member++)
            {
                var connection = _connections[members[member]];
                connections[side][member] = connection;
                names[side][member] = connection.Player.Character.Name;
                characterIds[side][member] = connection.Session?.Loaded?.Id ?? 0L;
            }
        }

        var match = new ArenaMatch(++_nextMatchId, first.Mode, first.Band, [first.Members, second.Members], names, [first.Origins, second.Origins], characterIds);
        _matches.Add(match);
        var field = GetField(ArenaRules.FieldCode);
        for (var side = 0; side < 2; side++)
        {
            var foes = string.Join(", ", match.Names[1 - side]);
            for (var member = 0; member < connections[side].Length; member++)
            {
                var connection = connections[side][member];
                var player = connection.Player;
                player.ArenaQueued = null;
                player.Arena = new ArenaSeat(match, side, member);
                player.ClearAttackTarget();
                player.Dismount();
                var place = field.Terrain.PlaceOnFloor(ArenaRules.Place(side, member));
                Relocate(connection, ArenaRules.FieldCode, place, inVillage: false);
                player.EnterField(place, inVillage: false);
                player.Heal(player.MaxLife);
                Notify(player.EntityId, ChatKind.System, GameText.Of(GameTextId.ArenaStarted, ArenaRules.ModeName(match.Mode), foes));
            }
        }
    }

    private void StepMatch(ArenaMatch match, float dtMs)
    {
        match.Step(dtMs);
        for (var side = 0; side < 2; side++)
        {
            for (var member = 0; member < match.Sides[side].Length; member++)
            {
                var inWorld = _connections.TryGetValue(match.Sides[side][member], out var connection) && connection.Player.Arena?.Match == match;
                if (inWorld && connection!.FieldId != ArenaRules.FieldCode)
                {
                    // Left the arena's field on its own: it stays where it went, out of the match.
                    connection.Player.Arena = null;
                    inWorld = false;
                }

                if (!match.IsOut(side, member) && !(inWorld && !connection!.Player.IsDead))
                {
                    match.TakeOut(side, member);
                }
            }
        }

        if (ArenaRules.Outcome(match.Standing(0), match.Standing(1), match.Damage[0], match.Damage[1], match.ElapsedMs >= ArenaRules.MatchMs) is { } outcome)
        {
            EndMatch(match, outcome);
        }
    }

    // A blow left the player at 1 life: it stands out of its match until the match ends.
    private void TakeOutOfMatch(ArenaSeat seat)
    {
        if (seat.Out)
        {
            return;
        }

        seat.Match.TakeOut(seat.Side, seat.Member);
        Notify(seat.Match.Sides[seat.Side][seat.Member], ChatKind.System, GameText.Of(GameTextId.ArenaOut));
    }

    // first: the first side's outcome.
    private void EndMatch(ArenaMatch match, ArenaOutcome first)
    {
        _matches.Remove(match);
        var entries = new List<ArenaResultEntry>();
        for (var side = 0; side < 2; side++)
        {
            var outcome = side == 0 ? first : ArenaRules.Other(first);
            for (var member = 0; member < match.Sides[side].Length; member++)
            {
                entries.Add(new ArenaResultEntry(match.CharacterIds[side][member], match.Names[side][member], outcome));
                if (_connections.TryGetValue(match.Sides[side][member], out var connection) && connection.Player.Arena?.Match == match)
                {
                    SendBack(connection, match.Origins[side][member]);
                    Notify(connection.Player.EntityId, ChatKind.System, outcome switch
                    {
                        ArenaOutcome.Won => GameText.Of(GameTextId.ArenaWon, ArenaRules.WinPoints),
                        ArenaOutcome.Lost => GameText.Of(GameTextId.ArenaLost, ArenaRules.LossPoints),
                        _ => GameText.Of(GameTextId.ArenaDraw)
                    });
                }
            }
        }

        var mode = ArenaRules.ModeName(match.Mode);
        var (winners, losers) = first == ArenaOutcome.Lost ? (1, 0) : (0, 1);
        NotifyEveryone(first == ArenaOutcome.Draw
            ? GameText.Of(GameTextId.ArenaDrawNotice, mode, string.Join(", ", match.Names[0]), string.Join(", ", match.Names[1]))
            : GameText.Of(GameTextId.ArenaNotice, mode, string.Join(", ", match.Names[winners]), string.Join(", ", match.Names[losers])));
        ArenaResultRecorded?.Invoke(new ArenaResult(ArenaWeek(), match.Band, match.Mode, entries));
    }

    // The Monday the world clock's server-local week started on: results count for it, prizes pay the one before.
    public DateOnly ArenaWeek() => ArenaRules.WeekStart(DateOnly.FromDateTime(_clock.GetLocalNow().DateTime));

    // The last finished week, whose prizes and titles are settled.
    public DateOnly LastArenaWeek() => ArenaWeek().AddDays(-7);

    // Back where the player queued or registered from (an arena match, the Battle Royale), with its whole life (a dead one
    // stands up there).
    private void SendBack(Connection connection, (int FieldId, Vector3 Position) origin)
    {
        var player = connection.Player;
        player.Arena = null;
        player.ClearAttackTarget();
        var inVillage = _catalog.TryGetFieldByCode(origin.FieldId, out var definition) && definition.State == FieldState.Village;
        Relocate(connection, origin.FieldId, origin.Position, inVillage);
        player.EnterField(origin.Position, inVillage);
        if (player.IsDead)
        {
            player.Revive(player.MaxLife, Simulation.Progression.DeathPenalty.RestartGuardMs);
        }
        else
        {
            player.Heal(player.MaxLife);
        }
    }

    // What a player's HUD shows of the arena, null when it neither queues nor plays.
    private static ArenaView? ArenaViewOf(WorldPlayer player) =>
        player.Arena is { } seat ? seat.Match.ViewOf(seat.Side, seat.Member)
        : player.ArenaQueued is { } entry ? entry.Mode == ArenaMode.Solo ? QueuedSolo : QueuedTrio
        : null;
}
