namespace PristonTale.Simulation.Events;

// What a character's events and rewards hold, as stored: the welcome campaign's first and last counted day, its login
// days and claimed days (a bit an entry index), the week (its Monday) and the day the weekly and daily progress belong
// to, with their progress and claims, and the level race's claims.
public sealed record RewardEventState(
    int WelcomeFirstDay,
    int WelcomeLastDay,
    int WelcomeDays,
    int WelcomeClaimed,
    int WeekStart,
    IReadOnlyList<int> Weekly,
    int WeeklyClaimed,
    int Day,
    IReadOnlyList<int> Daily,
    int DailyClaimed,
    int LevelClaimed)
{
    public static readonly RewardEventState Empty = new(0, 0, 0, 0, 0, [], 0, 0, [], 0, 0);
}

// A character's events and rewards (RewardEvents). Periods only move forward: a count or a claim dated in a later week
// or day starts that period afresh. Revision moves whenever what the window shows changes other than a goal's count
// below its target, so the world sends the window again only then.
public sealed class RewardEventBook
{
    private readonly int[] _weekly = new int[RewardEvents.MaxEntries];
    private readonly int[] _daily = new int[RewardEvents.MaxEntries];
    private int _levelsReached = -1;
    private int _observedDay;

    public int WelcomeFirstDay { get; private set; }
    public int WelcomeLastDay { get; private set; }
    public int WelcomeDays { get; private set; }
    public int WelcomeClaimed { get; private set; }
    public int WeekStart { get; private set; }
    public int WeeklyClaimed { get; private set; }
    public int Day { get; private set; }
    public int DailyClaimed { get; private set; }
    public int LevelClaimed { get; private set; }
    public long Revision { get; private set; }

    public RewardEventState Capture() => new(WelcomeFirstDay, WelcomeLastDay, WelcomeDays, WelcomeClaimed, WeekStart,
        _weekly.AsSpan(0, RewardEvents.Weekly.Entries.Count).ToArray(), WeeklyClaimed, Day, _daily.AsSpan(0, RewardEvents.Daily.Entries.Count).ToArray(),
        DailyClaimed, LevelClaimed);

    public void Restore(RewardEventState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.WelcomeDays < 0 || state.WelcomeDays > RewardEvents.Welcome.Entries.Count
            || state.WelcomeFirstDay < 0 || state.WelcomeLastDay < state.WelcomeFirstDay
            || state.WeekStart < 0 || state.WeekStart % RewardEvents.DaysPerWeek != 0 || state.Day < 0
            || state.Weekly.Count > RewardEvents.Weekly.Entries.Count || state.Daily.Count > RewardEvents.Daily.Entries.Count)
        {
            throw new InvalidDataException("Saved reward events are out of range.");
        }

        (WelcomeFirstDay, WelcomeLastDay, WelcomeDays) = (state.WelcomeFirstDay, state.WelcomeLastDay, state.WelcomeDays);
        WelcomeClaimed = Mask(state.WelcomeClaimed, RewardEvents.Welcome);
        (WeekStart, WeeklyClaimed) = (state.WeekStart, Mask(state.WeeklyClaimed, RewardEvents.Weekly));
        (Day, DailyClaimed) = (state.Day, Mask(state.DailyClaimed, RewardEvents.Daily));
        LevelClaimed = Mask(state.LevelClaimed, RewardEvents.LevelRace);
        Fill(_weekly, state.Weekly, RewardEvents.Weekly);
        Fill(_daily, state.Daily, RewardEvents.Daily);
        Revision++;
    }

    public void CopyFrom(RewardEventBook other)
    {
        (WelcomeFirstDay, WelcomeLastDay, WelcomeDays, WelcomeClaimed) = (other.WelcomeFirstDay, other.WelcomeLastDay, other.WelcomeDays, other.WelcomeClaimed);
        (WeekStart, WeeklyClaimed, Day, DailyClaimed, LevelClaimed) = (other.WeekStart, other.WeeklyClaimed, other.Day, other.DailyClaimed, other.LevelClaimed);
        other._weekly.CopyTo(_weekly, 0);
        other._daily.CopyTo(_daily, 0);
        Revision = other.Revision;
    }

    public bool WelcomeOpen(RewardDate date) => WelcomeFirstDay != 0 && date.Day < WelcomeFirstDay + RewardEvents.WelcomeCampaignDays;

    // Counts the day as a login day of the welcome campaign: the first day starts it, a day already counted or a day
    // after the campaign counts nothing, and at most seven count.
    public void CountLogin(RewardDate date)
    {
        if (WelcomeFirstDay == 0)
        {
            WelcomeFirstDay = date.Day;
        }

        if (!WelcomeOpen(date) || date.Day <= WelcomeLastDay || WelcomeDays >= RewardEvents.Welcome.Entries.Count)
        {
            return;
        }

        WelcomeLastDay = date.Day;
        WelcomeDays++;
        Revision++;
    }

    // Adds amount to the weekly and daily goals of the kind, from their tracks' MinLevel on.
    public void Count(RewardGoal goal, int amount, RewardDate date, int level)
    {
        if (amount <= 0)
        {
            return;
        }

        Roll(date);
        CountTrack(RewardEvents.Weekly, _weekly, WeeklyClaimed, goal, amount, level);
        CountTrack(RewardEvents.Daily, _daily, DailyClaimed, goal, amount, level);
    }

    // Moves the revision when a level target was passed or the day changed since the last look, so the window follows.
    public void Observe(int level, RewardDate date)
    {
        var reached = 0;
        foreach (var entry in RewardEvents.LevelRace.Entries)
        {
            reached += level >= entry.Target ? 1 : 0;
        }

        if (reached != _levelsReached || date.Day != _observedDay)
        {
            (_levelsReached, _observedDay) = (reached, date.Day);
            Revision++;
        }
    }

    public int Progress(RewardTrack track, int index, RewardDate date, int level)
    {
        var entry = RewardEvents.Of(track).Entries[index];
        return track switch
        {
            RewardTrack.Welcome => index < WelcomeDays ? 1 : 0,
            RewardTrack.Weekly => WeekStart == date.WeekStart ? _weekly[index] : 0,
            RewardTrack.Daily => Day == date.Day ? _daily[index] : 0,
            _ => Math.Min(level, entry.Target)
        };
    }

    public RewardEntryState State(RewardTrack track, int index, RewardDate date, int level)
    {
        if ((Claimed(track, date) & 1 << index) != 0)
        {
            return RewardEntryState.Claimed;
        }

        if (track == RewardTrack.Welcome)
        {
            return index < WelcomeDays ? RewardEntryState.Ready : index == WelcomeDays ? RewardEntryState.Next : RewardEntryState.Locked;
        }

        var definition = RewardEvents.Of(track);
        return level >= definition.MinLevel && Progress(track, index, date, level) >= definition.Entries[index].Target
            ? RewardEntryState.Ready
            : RewardEntryState.Locked;
    }

    public RewardClaimStatus Check(RewardTrack track, int index, RewardDate date, int level)
    {
        if (index < 0 || index >= RewardEvents.Of(track).Entries.Count)
        {
            return RewardClaimStatus.Unknown;
        }

        if (track == RewardTrack.Welcome && !WelcomeOpen(date))
        {
            return RewardClaimStatus.Expired;
        }

        return State(track, index, date, level) switch
        {
            RewardEntryState.Claimed => RewardClaimStatus.AlreadyClaimed,
            RewardEntryState.Ready => RewardClaimStatus.Ok,
            _ => level < RewardEvents.Of(track).MinLevel ? RewardClaimStatus.LevelTooLow : RewardClaimStatus.NotReady
        };
    }

    // Marks the entry claimed in the period of the date; a claim of a period the book already left marks nothing.
    public void MarkClaimed(RewardTrack track, int index, RewardDate date)
    {
        Roll(date);
        var bit = 1 << index;
        switch (track)
        {
            case RewardTrack.Welcome:
                WelcomeClaimed |= bit;
                break;
            case RewardTrack.Weekly when WeekStart == date.WeekStart:
                WeeklyClaimed |= bit;
                break;
            case RewardTrack.Daily when Day == date.Day:
                DailyClaimed |= bit;
                break;
            case RewardTrack.LevelRace:
                LevelClaimed |= bit;
                break;
            default:
                return;
        }

        Revision++;
    }

    private int Claimed(RewardTrack track, RewardDate date) => track switch
    {
        RewardTrack.Welcome => WelcomeClaimed,
        RewardTrack.Weekly => WeekStart == date.WeekStart ? WeeklyClaimed : 0,
        RewardTrack.Daily => Day == date.Day ? DailyClaimed : 0,
        _ => LevelClaimed
    };

    private void Roll(RewardDate date)
    {
        if (date.WeekStart > WeekStart)
        {
            WeekStart = date.WeekStart;
            WeeklyClaimed = 0;
            Array.Clear(_weekly);
            Revision++;
        }

        if (date.Day > Day)
        {
            Day = date.Day;
            DailyClaimed = 0;
            Array.Clear(_daily);
            Revision++;
        }
    }

    private void CountTrack(RewardTrackDefinition definition, int[] progress, int claimed, RewardGoal goal, int amount, int level)
    {
        if (level < definition.MinLevel)
        {
            return;
        }

        for (var index = 0; index < definition.Entries.Count; index++)
        {
            var target = definition.Entries[index].Target;
            if (definition.Entries[index].Goal != goal || progress[index] >= target || (claimed & 1 << index) != 0)
            {
                continue;
            }

            progress[index] = (int)Math.Min(target, (long)progress[index] + amount);
            if (progress[index] >= target)
            {
                Revision++;
            }
        }
    }

    private static int Mask(int claimed, RewardTrackDefinition definition) =>
        (claimed & ~((1 << definition.Entries.Count) - 1)) == 0 ? claimed : throw new InvalidDataException("Saved reward claims name unknown entries.");

    private static void Fill(int[] progress, IReadOnlyList<int> saved, RewardTrackDefinition definition)
    {
        Array.Clear(progress);
        for (var index = 0; index < saved.Count; index++)
        {
            progress[index] = saved[index] >= 0 ? Math.Min(saved[index], definition.Entries[index].Target)
                : throw new InvalidDataException("Saved reward progress is negative.");
        }
    }
}
