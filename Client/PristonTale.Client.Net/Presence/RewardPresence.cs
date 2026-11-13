using PristonTale.Contracts.Texts;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;

namespace PristonTale.Client.Net.Presence;

public sealed record RewardItemInfo(uint Code, int Count, string Name);

// One entry of a tab: what it counts (its goal and target; a level for the level race), how far the player is, its state
// and what it gives.
public sealed record RewardEntryInfo(RewardGoal Goal, RewardEntryState State, int Progress, int Target, long Gold, IReadOnlyList<RewardItemInfo> Items);

// A tab as the server last sent it: SecondsLeft to its end or reset when it was received (RewardTrackView.NoDeadline for
// none), its entries in order, how many are ready to claim and how many are reached (ready or claimed).
public sealed record RewardTrackInfo(RewardTrack Track, int MinLevel, long SecondsLeft, IReadOnlyList<RewardEntryInfo> Entries)
{
    public int Ready { get; } = Entries.Count(static entry => entry.State == RewardEntryState.Ready);

    public int Reached { get; } = Entries.Count(static entry => entry.State is RewardEntryState.Ready or RewardEntryState.Claimed);
}

// The events and rewards window: the tabs shown (the welcome campaign is absent once it ended) and every ready reward.
public sealed record RewardEventsInfo(IReadOnlyList<RewardTrackInfo> Tracks)
{
    public static readonly RewardEventsInfo None = new([]);

    public int Ready { get; } = Tracks.Sum(static track => track.Ready);

    public RewardTrackInfo? Find(RewardTrack track)
    {
        foreach (var shown in Tracks)
        {
            if (shown.Track == track)
            {
                return shown;
            }
        }

        return null;
    }
}

// How a request of the window ended: the entries a claim took (a bit an index) and the line for the player.
public sealed record RewardEventsResultInfo(RewardEventsAction Action, RewardTrack Track, RewardClaimStatus Status, int Claimed, GameText Message);
