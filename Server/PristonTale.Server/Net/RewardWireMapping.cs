using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;
using PristonTale.Simulation.Items;

namespace PristonTale.Server.Net;

// The events and rewards window on the wire: item names in the receiver's language.
internal static class RewardWireMapping
{
    public static RewardEventsPacket ToPacket(RewardBoard board, ItemCatalog items)
    {
        var tracks = new RewardTrackWire[board.Tracks.Count];
        for (var index = 0; index < tracks.Length; index++)
        {
            var track = board.Tracks[index];
            var entries = new RewardEntryWire[track.Entries.Count];
            for (var entry = 0; entry < entries.Length; entry++)
            {
                var view = track.Entries[entry];
                var rewards = new RewardItemWire[view.Items.Count];
                for (var item = 0; item < rewards.Length; item++)
                {
                    var (code, count) = view.Items[item];
                    rewards[item] = new RewardItemWire(code, (ushort)Math.Clamp(count, 0, ushort.MaxValue),
                        items.TryGet(code, out var definition) ? definition.Name : ItemCodes.KeyOf(code));
                }

                entries[entry] = new RewardEntryWire((byte)view.Goal, (byte)view.State, (uint)Math.Max(0, view.Progress), (uint)Math.Max(0, view.Target),
                    (ulong)Math.Max(0, view.Gold), rewards);
            }

            ulong? seconds = track.SecondsLeft == RewardTrackView.NoDeadline ? null : (ulong)Math.Max(0, track.SecondsLeft);
            tracks[index] = new RewardTrackWire((byte)track.Track, (ushort)Math.Clamp(track.MinLevel, 0, ushort.MaxValue), seconds, entries);
        }

        return new RewardEventsPacket(tracks);
    }
}
