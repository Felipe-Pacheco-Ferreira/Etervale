using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Items.Manufacture;

namespace PristonTale.Simulation.Events;

// The tabs of the events and rewards window, in the order the window lists them.
public enum RewardTrack : byte
{
    Welcome = 0,
    Weekly = 1,
    Daily = 2,
    LevelRace = 3
}

// What an entry counts toward its target.
public enum RewardGoal : byte
{
    LoginDays = 0,
    Level = 1,
    MonsterKills = 2,
    BellatraEntries = 3,
    MonsterDrops = 4,
    Sheltoms = 5,
    Potions = 6
}

public enum RewardEntryState : byte
{
    Locked = 0,
    Next = 1,
    Ready = 2,
    Claimed = 3
}

public enum RewardClaimStatus : byte
{
    Ok = 0,
    NotReady = 1,
    AlreadyClaimed = 2,
    Expired = 3,
    NoSpace = 4,
    OverWeight = 5,
    GoldLimit = 6,
    LevelTooLow = 7,
    Busy = 8,
    Unknown = 9
}

// Count units of the item with the catalog key; ClassCostume stands for the claimer's class costume.
public readonly record struct RewardItem(string Key, int Count = 1)
{
    public const string ClassCostume = "class-costume";
}

public sealed record RewardEntry(RewardGoal Goal, int Target, IReadOnlyList<RewardItem> Items, long Gold = 0);

// A tab's entries; goals count and may be claimed only from MinLevel on.
public sealed record RewardTrackDefinition(RewardTrack Track, int MinLevel, IReadOnlyList<RewardEntry> Entries);

// The events and rewards of every character, kept per character with the character:
// - Welcome: each distinct server-local day the character enters the world (or plays across midnight) counts one login
//   day, consecutive or not, up to seven; day N may be claimed once N days were counted. The campaign lasts
//   WelcomeCampaignDays from the first counted day; what is unclaimed then is lost and the tab hides.
// - Weekly and Daily: goals counted from MinLevel on, reset every Monday 00:00 and every day 00:00 server time.
// - LevelRace: a reward once per character for each level reached, without deadline.
public static class RewardEvents
{
    public const int WelcomeCampaignDays = 30;
    public const int MaxEntries = 8;
    public const int SecondsPerDay = 86_400;
    public const int DaysPerWeek = 7;

    public static readonly RewardTrackDefinition Welcome = new(RewardTrack.Welcome, 1,
    [
        Login(new RewardItem("BI108", 3)),
        Login(new RewardItem("BI113")),
        Login(new RewardItem("BI169")),
        Login(new RewardItem("BI119", 2)),
        Login(new RewardItem("BI258", 3)),
        Login(new RewardItem("BI111", 3)),
        Login(new RewardItem(RewardItem.ClassCostume))
    ]);

    public static readonly RewardTrackDefinition Weekly = new(RewardTrack.Weekly, 25,
    [
        new RewardEntry(RewardGoal.MonsterKills, 3000, [new RewardItem("BI112")], 100_000),
        new RewardEntry(RewardGoal.BellatraEntries, 7, [new RewardItem("BI113"), new RewardItem("BI104", 3)]),
        new RewardEntry(RewardGoal.MonsterDrops, 300, [new RewardItem("BI111", 3)], 50_000)
    ]);

    public static readonly RewardTrackDefinition Daily = new(RewardTrack.Daily, 20,
    [
        new RewardEntry(RewardGoal.MonsterKills, 300, [new RewardItem("PL102", 20)], 10_000),
        new RewardEntry(RewardGoal.Sheltoms, 10, [new RewardItem("BI111")]),
        new RewardEntry(RewardGoal.Potions, 30, [new RewardItem("PM102", 20)])
    ]);

    public static readonly RewardTrackDefinition LevelRace = new(RewardTrack.LevelRace, 1,
    [
        new RewardEntry(RewardGoal.Level, 10, [new RewardItem("BI108", 3), new RewardItem("BI104", 3)]),
        new RewardEntry(RewardGoal.Level, 25, [new RewardItem("BI113")], 20_000),
        new RewardEntry(RewardGoal.Level, 40, [new RewardItem("BI110", 3)], 100_000),
        new RewardEntry(RewardGoal.Level, 55, [new RewardItem("BI169")], 200_000),
        new RewardEntry(RewardGoal.Level, 70, [new RewardItem("BI110", 3)], 500_000),
        new RewardEntry(RewardGoal.Level, 80, [new RewardItem("BI111", 4)], 500_000),
        new RewardEntry(RewardGoal.Level, 90, [new RewardItem("BI161")], 1_000_000)
    ]);

    public static readonly IReadOnlyList<RewardTrackDefinition> Tracks = [Welcome, Weekly, Daily, LevelRace];

    public static RewardTrackDefinition Of(RewardTrack track) => Tracks[(int)track];

    public static bool IsTrack(int track) => track >= 0 && track < Tracks.Count;

    // The catalog key a reward item names for the class.
    public static string KeyFor(RewardItem item, PlayerClass playerClass) =>
        item.Key == RewardItem.ClassCostume ? ManufactureWork.Costume(playerClass) : item.Key;

    public static GameText Message(RewardClaimStatus status) => status switch
    {
        RewardClaimStatus.Ok => GameText.Of(GameTextId.RewardClaimed),
        RewardClaimStatus.AlreadyClaimed => GameText.Of(GameTextId.RewardAlreadyClaimed),
        RewardClaimStatus.Expired => GameText.Of(GameTextId.RewardExpired),
        RewardClaimStatus.NoSpace => GameText.Of(GameTextId.NoSpace),
        RewardClaimStatus.OverWeight => GameText.Of(GameTextId.OverWeight),
        RewardClaimStatus.GoldLimit => GameText.Of(GameTextId.RewardGoldLimit),
        RewardClaimStatus.LevelTooLow => GameText.Of(GameTextId.RewardLevelTooLow),
        RewardClaimStatus.Busy => GameText.Of(GameTextId.RewardBusy),
        _ => GameText.Of(GameTextId.RewardNotReady)
    };

    private static RewardEntry Login(RewardItem item) => new(RewardGoal.LoginDays, 1, [item]);
}

// A moment on the server's local calendar: the day (DateOnly.DayNumber) and the seconds since its midnight.
public readonly record struct RewardDate(int Day, int SecondOfDay)
{
    // DateOnly day number 0 (1 January of year 1) is a Monday.
    public int WeekStart => Day - Day % RewardEvents.DaysPerWeek;

    public static RewardDate Of(DateTimeOffset local) =>
        new(DateOnly.FromDateTime(local.DateTime).DayNumber, (int)local.TimeOfDay.TotalSeconds);

    // Seconds from this moment to the midnight that starts the day.
    public long SecondsUntil(int day) => (long)(day - Day) * RewardEvents.SecondsPerDay - SecondOfDay;
}
