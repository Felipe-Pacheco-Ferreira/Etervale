using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Events;

// How a claim ended: the entries it claimed (a bit an index) and the gold they gave.
public readonly record struct RewardClaimResult(RewardClaimStatus Status, int Claimed, long Gold);

public readonly record struct RewardItemView(uint Code, int Count);

public sealed record RewardEntryView(RewardGoal Goal, RewardEntryState State, int Progress, int Target, IReadOnlyList<RewardItemView> Items, long Gold);

// A tab as its owner sees it; SecondsLeft is the time to its end or reset, NoDeadline for none.
public sealed record RewardTrackView(RewardTrack Track, int MinLevel, long SecondsLeft, IReadOnlyList<RewardEntryView> Entries)
{
    public const long NoDeadline = -1;
}

// The window: the tabs shown (the welcome campaign hides once it ended), at the book's revision.
public sealed record RewardBoard(long Revision, IReadOnlyList<RewardTrackView> Tracks);

// Claims and the window of a character's events and rewards (RewardEventBook).
public static class RewardClaims
{
    // The index that claims every entry ready in a track.
    public const int All = -1;

    // Claims the entry (or, with index All, every entry ready in the track, in order, until one does not fit) on the
    // candidate: its items into the bag or none, its gold, and the claim. A partial claim of all answers Ok.
    public static RewardClaimResult Claim(PlayerCharacter candidate, IQuestItemSource items, RewardTrack track, int index, RewardDate date)
    {
        if (index != All)
        {
            return ClaimOne(candidate, items, track, index, date);
        }

        var (claimed, gold, first) = (0, 0L, RewardClaimStatus.NotReady);
        for (var entry = 0; entry < RewardEvents.Of(track).Entries.Count; entry++)
        {
            if (candidate.Events.State(track, entry, date, candidate.Level) != RewardEntryState.Ready)
            {
                continue;
            }

            var result = ClaimOne(candidate, items, track, entry, date);
            if (result.Status != RewardClaimStatus.Ok)
            {
                first = claimed == 0 ? result.Status : first;
                break;
            }

            (claimed, gold) = (claimed | result.Claimed, gold + result.Gold);
        }

        return claimed == 0 ? new RewardClaimResult(first, 0, 0) : new RewardClaimResult(RewardClaimStatus.Ok, claimed, gold);
    }

    public static RewardBoard Board(PlayerCharacter character, RewardDate date, ItemCatalog items)
    {
        var book = character.Events;
        var tracks = new List<RewardTrackView>(RewardEvents.Tracks.Count);
        foreach (var definition in RewardEvents.Tracks)
        {
            var secondsLeft = definition.Track switch
            {
                RewardTrack.Welcome => date.SecondsUntil(book.WelcomeFirstDay + RewardEvents.WelcomeCampaignDays),
                RewardTrack.Weekly => date.SecondsUntil(date.WeekStart + RewardEvents.DaysPerWeek),
                RewardTrack.Daily => date.SecondsUntil(date.Day + 1),
                _ => RewardTrackView.NoDeadline
            };
            if (definition.Track == RewardTrack.Welcome && !book.WelcomeOpen(date))
            {
                continue;
            }

            var entries = new RewardEntryView[definition.Entries.Count];
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = definition.Entries[index];
                entries[index] = new RewardEntryView(entry.Goal, book.State(definition.Track, index, date, character.Level),
                    book.Progress(definition.Track, index, date, character.Level), entry.Target, ItemsOf(entry, character, items), entry.Gold);
            }

            tracks.Add(new RewardTrackView(definition.Track, definition.MinLevel, secondsLeft, entries));
        }

        return new RewardBoard(book.Revision, tracks);
    }

    private static RewardClaimResult ClaimOne(PlayerCharacter candidate, IQuestItemSource items, RewardTrack track, int index, RewardDate date)
    {
        var status = candidate.Events.Check(track, index, date, candidate.Level);
        if (status != RewardClaimStatus.Ok)
        {
            return new RewardClaimResult(status, 0, 0);
        }

        var entry = RewardEvents.Of(track).Entries[index];
        var grants = new List<QuestGrant>(entry.Items.Count);
        foreach (var item in entry.Items)
        {
            grants.Add(new QuestGrant(ItemCodes.Parse(RewardEvents.KeyFor(item, candidate.Definition.Class)), item.Count));
        }

        var created = QuestInventory.Create(items, candidate, grants);
        var fits = QuestInventory.Check(candidate, created);
        if (fits != QuestGrantCheck.Fits)
        {
            return new RewardClaimResult(fits == QuestGrantCheck.OverWeight ? RewardClaimStatus.OverWeight : RewardClaimStatus.NoSpace, 0, 0);
        }

        if (!MoneyLimits.CanCarry(candidate, entry.Gold) || !candidate.TryChangeMoney(entry.Gold))
        {
            return new RewardClaimResult(RewardClaimStatus.GoldLimit, 0, 0);
        }

        QuestInventory.TryGiveAll(candidate, created);
        candidate.Events.MarkClaimed(track, index, date);
        return new RewardClaimResult(RewardClaimStatus.Ok, 1 << index, entry.Gold);
    }

    private static RewardItemView[] ItemsOf(RewardEntry entry, PlayerCharacter character, ItemCatalog items)
    {
        var count = 0;
        var views = new RewardItemView[entry.Items.Count];
        foreach (var item in entry.Items)
        {
            if (ItemCodes.TryParse(RewardEvents.KeyFor(item, character.Definition.Class), out var code) && items.TryGet(code, out _))
            {
                views[count++] = new RewardItemView(code, item.Count);
            }
        }

        return count == views.Length ? views : views[..count];
    }
}
