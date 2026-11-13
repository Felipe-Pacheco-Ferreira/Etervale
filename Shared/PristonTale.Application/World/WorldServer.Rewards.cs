using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Events;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;

namespace PristonTale.Application.World;

// A claim prepared on the tick: the track, what it claimed and the date it was claimed on (its period).
public sealed record RewardClaim(RewardTrack Track, RewardClaimResult Result, RewardDate Date);

// The events and rewards of the players (RewardEvents): the server-local date of the tick, the login day of a player
// entering or playing across midnight, the window each snapshot carries when it changed, and the claims.
public sealed partial class WorldServer
{
    private RewardDate _rewardDate;

    // Tick only, before the fields step: a new day counts as a login day for everyone playing.
    private void StepRewardCalendar(DateTimeOffset localNow)
    {
        var date = RewardDate.Of(localNow);
        var newDay = _rewardDate.Day != 0 && date.Day != _rewardDate.Day;
        _rewardDate = date;
        if (!newDay)
        {
            return;
        }

        foreach (var connection in _connections.Values)
        {
            connection.Player.Character.Events.CountLogin(date);
        }
    }

    // Tick only: an item a monster dropped, picked up, counts for the drop goals, a sheltom also for the sheltom goals.
    private void CountPickup(PlayerCharacter character, ItemInstance? item)
    {
        if (item is null)
        {
            return;
        }

        character.Events.Count(RewardGoal.MonsterDrops, 1, _rewardDate, character.Level);
        if (item.Definition.Kind == ItemKind.Sheltom)
        {
            character.Events.Count(RewardGoal.Sheltoms, item.Count, _rewardDate, character.Level);
        }
    }

    // Field phase: the window again only when the book's revision moved.
    private RewardBoard? RewardsOf(Connection connection)
    {
        var character = connection.Player.Character;
        character.Events.Observe(character.Level, _rewardDate);
        if (character.Events.Revision != connection.RewardRevision)
        {
            connection.RewardRevision = character.Events.Revision;
            connection.Rewards = RewardClaims.Board(character, _rewardDate, _worldData.Current.Items);
        }

        return connection.Rewards;
    }

    // The window as it is now, with every count; null when the session does not play.
    public Task<RewardBoard?> RewardBoardAsync(PlayerSession session) =>
        EnqueueAsync<RewardBoard?>(session.EntityId, () => TryGetPlaying(session, out var connection)
            ? RewardClaims.Board(connection.Player.Character, RewardDate.Of(_clock.GetLocalNow()), _worldData.Current.Items)
            : null);

    // A claim (RewardClaims.All: every entry ready in the track) on a copy of the character; a refusal writes nothing.
    public Task<PreparedCharacterOperation<RewardClaim>?> PrepareRewardClaimAsync(PlayerSession session, RewardTrack track, int index) =>
        EnqueueAsync<PreparedCharacterOperation<RewardClaim>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var original = connection.Player.Character;
            var date = RewardDate.Of(_clock.GetLocalNow());
            var candidate = original.CloneForInventoryOperation();
            var result = RewardClaims.Claim(candidate, new CatalogQuestItemSource(_worldData.Current.Items), track, index, date);
            var claim = new RewardClaim(track, result, date);
            return result.Status == RewardClaimStatus.Ok
                ? new PreparedCharacterOperation<RewardClaim>(original, candidate,
                    CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, connection.Player.Position), claim)
                : new PreparedCharacterOperation<RewardClaim>(original, null, null, claim);
        });

    // The items and gold of the claim, and the claims marked on the live book (whose counts moved on since the prepare).
    public Task<bool> CommitRewardClaimAsync(PlayerSession session, PreparedCharacterOperation<RewardClaim> operation) =>
        EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, operation, "a reward claim");
            var (track, result, date) = operation.Outcome;
            operation.Original.CommitInventoryOperation(candidate);
            if (!operation.Original.TryChangeMoney(result.Gold))
            {
                throw new CommitInvariantException("The gold changed beyond capacity during a reward claim's commit.");
            }

            for (var index = 0; index < RewardEvents.MaxEntries; index++)
            {
                if ((result.Claimed & 1 << index) != 0)
                {
                    operation.Original.Events.MarkClaimed(track, index, date);
                }
            }

            return true;
        }, waitForCapacity: true);
}
