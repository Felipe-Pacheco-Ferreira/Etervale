using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.Events;

namespace PristonTale.Application.Services;

// The events and rewards window: its view, and claims through the path of every inventory change (the save gate, the
// copy made and checked on the tick, the commit, the background save). A claim refused for a bag without room changes
// nothing and stays claimable.
public sealed class RewardEventAppService(WorldServer world, ICharacterSaves saves)
{
    private readonly DurableOperations _durable = new(saves);

    public Task<RewardBoard?> ViewAsync(PlayerSession session) => world.RewardBoardAsync(session);

    // index: an entry of the track, or RewardClaims.All for every entry ready in it.
    public async Task<RewardClaimResult> ClaimAsync(PlayerSession session, RewardTrack track, int index, CancellationToken ct)
    {
        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        return await lease.RunAsync(
            (playing) => world.PrepareRewardClaimAsync(playing, track, index),
            world.CommitRewardClaimAsync,
            static prepared => prepared.Outcome.Result,
            static _ => new RewardClaimResult(RewardClaimStatus.Busy, 0, 0));
    }
}
