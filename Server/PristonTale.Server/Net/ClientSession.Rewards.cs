using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;

namespace PristonTale.Server.Net;

internal sealed partial class ClientSession
{
    // The events and rewards window: a claim answers its result, then every request the window as it is now.
    private async Task RewardEventsAsync(RewardEventsRequestPacket request)
    {
        if (_player is null || _session is not { } session)
        {
            return;
        }

        var rewards = _services.Rewards;
        if (request.Action != RewardEventsAction.Open)
        {
            var track = RewardEvents.IsTrack(request.Track) ? (RewardTrack)request.Track : RewardTrack.Welcome;
            var result = RewardEvents.IsTrack(request.Track)
                ? await rewards.ClaimAsync(session, track, request.Action == RewardEventsAction.ClaimAll ? RewardClaims.All : request.Index, CancellationToken.None)
                : new RewardClaimResult(RewardClaimStatus.Unknown, 0, 0);
            SendLarge(new RewardEventsResultPacket(request.Action, request.Track, (byte)result.Status, (byte)result.Claimed,
                RewardEvents.Message(result.Status)).Write, RewardEventsResultPacket.MaxSize);
        }

        if (await rewards.ViewAsync(session) is { } board)
        {
            var items = _services.LocalizedWorldData.Get(session.Language, _services.WorldData.Current).Items;
            SendLarge(RewardWireMapping.ToPacket(board, items).Write, RewardEventsPacket.MaxSize);
        }
    }
}
