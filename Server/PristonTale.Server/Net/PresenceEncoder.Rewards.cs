using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;

namespace PristonTale.Server.Net;

internal sealed partial class PresenceEncoder
{
    // The events and rewards window whenever the world published a new one (WorldServer.RewardsOf).
    private void SendRewards(Simulation.Events.RewardBoard? rewards, IPacketSink connection)
    {
        if (rewards is null || ReferenceEquals(rewards, _rewards))
        {
            return;
        }

        _rewards = rewards;
        SendSized(connection, RewardWireMapping.ToPacket(rewards, LocalizedData.Items).Write, RewardEventsPacket.MaxSize);
    }
}
