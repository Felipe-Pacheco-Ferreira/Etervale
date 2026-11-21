using System.Globalization;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Combat;

namespace PristonTale.Application.World;

// Duels (DuelRules) and the fields' fight results, on the tick thread. A challenge ("/duelo name") waits on the target
// (DuelRequest) until it answers or InviteMs passes; an accepted one starts at once around the point between the two.
// After the fields step, StepFights tells the murders the fields saw (PvpRules), ends the duels (a win a field saw, a
// duellist dead or gone from the field or the world, out of the arena OutsideMs in a row, or a draw at LimitMs) and
// steps the arena (WorldServer.Arena). Each duellist is told its result and every other player of the field the outcome
// (DuelNotice, DuelDrawNotice).
public sealed partial class WorldServer
{
    private readonly List<Duel> _duels = [];
    private readonly List<(WorldPlayer Killer, WorldPlayer Victim)> _murdersScratch = [];
    private readonly List<(WorldPlayer Attacker, WorldPlayer Target)> _sparedScratch = [];

    public Task<DuelChallenge?> ChallengeDuelAsync(PlayerSession session, string targetName) =>
        EnqueueAsync<DuelChallenge?>(session.EntityId, () => TryGetPlaying(session, out var connection) ? Challenge(connection, targetName) : null);

    public Task<bool> AnswerDuelAsync(PlayerSession session, ulong fromId, bool accept) =>
        EnqueueAsync(session.EntityId, () => TryGetPlaying(session, out var connection) && Answer(connection, fromId, accept));

    private DuelChallenge Challenge(Connection connection, string targetName)
    {
        var player = connection.Player;
        var target = ConnectionNamed(targetName);
        var answer = target is null ? DuelChallenge.NotFound : MayDuel(connection, target);
        var name = answer == DuelChallenge.NotFound ? targetName : target!.Player.Character.Name;
        if (answer == DuelChallenge.Sent)
        {
            target!.Player.DuelRequest = new DuelRequest(player.EntityId, player.Character.Name, UnixNowMs + DuelRules.InviteMs);
        }

        Notify(player.EntityId, ChatKind.System, answer switch
        {
            DuelChallenge.Sent => GameText.Of(GameTextId.DuelChallenged, name),
            DuelChallenge.NotFound => GameText.Of(GameTextId.DuelNotFound, name),
            DuelChallenge.TooFar => GameText.Of(GameTextId.DuelTooFar, name),
            DuelChallenge.Field => GameText.Of(GameTextId.DuelField),
            DuelChallenge.Busy => GameText.Of(GameTextId.DuelBusy, name),
            _ => GameText.Of(GameTextId.DuelSelf)
        });
        return answer;
    }

    // Whether the challenger may duel the target now: another player, in the same hunting field and within the arena's
    // radius, neither dead nor duelling, the target not waiting on somebody else's challenge.
    private DuelChallenge MayDuel(Connection challenger, Connection target)
    {
        var player = challenger.Player;
        if (player.Hidden || target.Player.Hidden)
        {
            // A hidden game master neither fights nor is fought (FieldInstance.MayFight).
            return DuelChallenge.NotFound;
        }

        if (ReferenceEquals(target.Player, player))
        {
            return DuelChallenge.Self;
        }

        if (target.FieldId != challenger.FieldId || !DuelRules.InArena(player.Position, target.Player.Position))
        {
            return DuelChallenge.TooFar;
        }

        if (!_catalog.TryGetFieldByCode(challenger.FieldId, out var field) || !DuelRules.FieldAllows(field.State))
        {
            return DuelChallenge.Field;
        }

        return player.IsDead || target.Player.IsDead || player.Duel is not null || target.Player.Duel is not null || player.Arena is not null
            || target.Player.Arena is not null || player.ArenaQueued is not null || target.Player.ArenaQueued is not null
            || target.Player.DuelRequest is { } pending && pending.FromId != player.EntityId && pending.ExpiresMs >= UnixNowMs
            ? DuelChallenge.Busy
            : DuelChallenge.Sent;
    }

    private bool Answer(Connection connection, ulong fromId, bool accept)
    {
        var player = connection.Player;
        if (player.DuelRequest is not { } request || request.FromId != fromId)
        {
            return false;
        }

        player.DuelRequest = null;
        if (!_connections.TryGetValue(fromId, out var from) || request.ExpiresMs < UnixNowMs)
        {
            return false;
        }

        if (!accept)
        {
            Notify(fromId, ChatKind.System, GameText.Of(GameTextId.DuelRefused, player.Character.Name));
            return false;
        }

        if (MayDuel(from, connection) != DuelChallenge.Sent)
        {
            Notify(player.EntityId, ChatKind.System, GameText.Of(GameTextId.DuelBusy, from.Player.Character.Name));
            return false;
        }

        var duel = new Duel(from.Player, player, connection.FieldId);
        foreach (var duellist in (ReadOnlySpan<WorldPlayer>)[from.Player, player])
        {
            duellist.Duel = duel;
            duellist.ClearAttackTarget();
            Notify(duellist.EntityId, ChatKind.System, GameText.Of(GameTextId.DuelStarted, duel.Names[1 - duel.SideOf(duellist.EntityId)]));
        }

        _duels.Add(duel);
        return true;
    }

    // Tick thread, after the fields stepped.
    private void StepFights(float dtMs)
    {
        foreach (var field in _activeFields)
        {
            field.TakeFightResults(_murdersScratch, _sparedScratch);
        }

        foreach (var (killer, victim) in _murdersScratch)
        {
            Notify(killer.EntityId, ChatKind.System,
                GameText.Of(GameTextId.PvpMurder, victim.Character.Name, PvpRules.MurdererMinutes.ToString(CultureInfo.InvariantCulture)));
        }

        // A spared blow a later one already settled (a duel over, a player already out) is left alone.
        foreach (var (attacker, target) in _sparedScratch)
        {
            if (attacker.Duel is { } duel && attacker.DuelsWith(target))
            {
                EndDuel(duel, duel.SideOf(attacker.EntityId));
            }
            else if (target.Arena is { } seat && attacker.ArenaFoeOf(target))
            {
                TakeOutOfMatch(seat);
            }
            else if (target.Royale is { } royaleSeat && attacker.RoyaleFoeOf(target))
            {
                TakeOutOfRoyale(royaleSeat);
            }
        }

        _murdersScratch.Clear();
        _sparedScratch.Clear();
        for (var index = _duels.Count - 1; index >= 0; index--)
        {
            StepDuel(_duels[index], dtMs);
        }

        StepArena(dtMs);
        StepRoyale(dtMs);
    }

    private void StepDuel(Duel duel, float dtMs)
    {
        var first = Duellist(duel, 0);
        var second = Duellist(duel, 1);
        if (first is null || second is null)
        {
            EndDuel(duel, first is not null ? 0 : second is not null ? 1 : Draw);
            return;
        }

        duel.Step(dtMs, first.Position, second.Position);
        var firstOut = duel.OutsideMs(0) >= DuelRules.OutsideMs;
        var secondOut = duel.OutsideMs(1) >= DuelRules.OutsideMs;
        if (firstOut || secondOut)
        {
            EndDuel(duel, firstOut && secondOut ? Draw : firstOut ? 1 : 0);
        }
        else if (duel.ElapsedMs >= DuelRules.LimitMs)
        {
            EndDuel(duel, Draw);
        }
    }

    private const int Draw = -1;

    // A side still in the duel's field and alive; a dead one, a leaver or a traveller has lost.
    private WorldPlayer? Duellist(Duel duel, int side) =>
        _connections.TryGetValue(duel.Ids[side], out var connection) && connection.FieldId == duel.FieldId && !connection.Player.IsDead
        && ReferenceEquals(connection.Player.Duel, duel)
            ? connection.Player
            : null;

    // The winning side, or Draw.
    private void EndDuel(Duel duel, int winner)
    {
        if (!_duels.Remove(duel))
        {
            return;
        }

        for (var side = 0; side < 2; side++)
        {
            if (!_connections.TryGetValue(duel.Ids[side], out var connection) || !ReferenceEquals(connection.Player.Duel, duel))
            {
                continue;
            }

            connection.Player.Duel = null;
            connection.Player.ClearAttackTarget();
            var opponent = duel.Names[1 - side];
            Notify(duel.Ids[side], ChatKind.System, winner == Draw ? GameText.Of(GameTextId.DuelDraw, opponent)
                : GameText.Of(winner == side ? GameTextId.DuelWon : GameTextId.DuelLost, opponent));
        }

        var notice = winner == Draw
            ? GameText.Of(GameTextId.DuelDrawNotice, duel.Names[0], duel.Names[1])
            : GameText.Of(GameTextId.DuelNotice, duel.Names[winner], duel.Names[1 - winner]);
        foreach (var connection in _connections.Values)
        {
            if (connection.FieldId == duel.FieldId && Array.IndexOf(duel.Ids, connection.Player.EntityId) < 0)
            {
                Notify(connection.Player.EntityId, ChatKind.System, notice);
            }
        }
    }
}
