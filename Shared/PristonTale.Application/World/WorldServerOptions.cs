using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

public sealed record WorldServerOptions
{
    // Etervale's server-wide bonus on .inf experience; the original only had level-banded event bonuses on
    // its experience total, so this is a server setting rather than a ported constant.
    public const int DefaultExperienceMultiplier = 5;

    // A client normally has a few commands waiting for the next tick; the cap keeps one connection from filling
    // the queue shared by every player, which holds the full quota of WorldServer.CommandQueueCapacity / this players.
    public const int DefaultMaxPendingCommandsPerPlayer = 16;

    // A crash loses at most this much of what no operation queued for storage: experience, life, position.
    public const int DefaultCheckpointSeconds = 60;

    // The two event girls of the towns: Etervale has no use for their stat redistribution stand.
    public static readonly IReadOnlyList<string> DefaultRemovedNpcs = ["Event Girl", "Event Girl 2"];

    public static readonly WorldServerOptions Default = new();

    public int ExperienceMultiplier { get; init; } = DefaultExperienceMultiplier;

    public int ExperienceEventPercent { get; init; } = KillReward.NoEventPercent;

    public AgingSettings Aging { get; init; } = AgingSettings.Default;

    public Simulation.Items.Mixing.MixingSettings Mixing { get; init; } = Simulation.Items.Mixing.MixingSettings.Default;

    // Game minutes added to the game clock, to start the server at another hour of the day.
    public int ClockOffsetMinutes { get; init; }

    // Rain over the fields that take it on the original's schedule (WeatherSchedule), every WeatherCycleMinutes game
    // minutes.
    public bool Weather { get; init; } = true;

    // Bless Castle's league runs: players do not fight each other in the castle field.
    public bool CastleLeague { get; init; }

    // The arena's opening hours and the week's prizes (ArenaRules).
    public ArenaOptions Arena { get; init; } = ArenaOptions.Default;

    // The Battle Royale's hours and its prizes (RoyaleRules).
    public RoyaleOptions Royale { get; init; } = RoyaleOptions.Default;

    // The percent of a blow's damage players with the PvP mode on take from each other in a hunting field (PvpRules).
    public int PvpDamagePercent { get; init; } = Simulation.Combat.PvpRules.DefaultDamagePercent;

    public int WeatherCycleMinutes { get; init; } = WeatherSchedule.DefaultCycleMinutes;

    public int MaxPendingCommandsPerPlayer { get; init; } = DefaultMaxPendingCommandsPerPlayer;

    public bool GiveStarterGifts { get; init; } = true;

    // Every playing character is captured for storage once per this many seconds, each on its own tick.
    public int CheckpointSeconds { get; init; } = DefaultCheckpointSeconds;

    // The crystal event: 0 off, 1 from 12:00 to 23:59 server time, 2 all day; crystals then call their event monsters.
    public int CrystalEvent { get; init; }

    // Item events that replace drop rolls with event items (rates per 10000); all off by default.
    public ItemEventRates ItemEvents { get; init; } = ItemEventRates.None;

    public BellatraOptions Bellatra { get; init; } = BellatraOptions.Default;

    public ServerNoticeOptions Notices { get; init; } = ServerNoticeOptions.Default;

    // NPCs, by their definition name, that no field places.
    public IReadOnlyList<string> RemovedNpcs { get; init; } = DefaultRemovedNpcs;

    public WorldServerOptions Validated()
    {
        if (ExperienceMultiplier < 1)
        {
            throw new InvalidOperationException("World:ExperienceMultiplier must be at least 1.");
        }

        if (MaxPendingCommandsPerPlayer < 1)
        {
            throw new InvalidOperationException("World:MaxPendingCommandsPerPlayer must be at least 1.");
        }

        if (WeatherCycleMinutes < WeatherSchedule.MinimumCycleMinutes)
        {
            throw new InvalidOperationException($"World:WeatherCycleMinutes must be at least {WeatherSchedule.MinimumCycleMinutes}.");
        }

        if (CheckpointSeconds < 1)
        {
            throw new InvalidOperationException("World:CheckpointSeconds must be at least 1.");
        }

        if (CrystalEvent is < 0 or > 2)
        {
            throw new InvalidOperationException("World:CrystalEvent must be 0, 1 or 2.");
        }

        if (ExperienceEventPercent < 0 || Aging is null || Aging.PricePercent < 0 || Aging.FailurePercent < 0 || Aging.DoublePercent < 0)
        {
            throw new InvalidOperationException("Experience event and aging percentages must not be negative.");
        }

        if (Bellatra is null || !Enum.IsDefined(Bellatra.Mode) || Bellatra.Tax is < 0 or > 100 || Bellatra.ScoreDivisor < 1)
        {
            throw new InvalidOperationException("Bellatra's mode must be 0, 1 or 2, its tax 0 to 100 and its score divisor at least 1.");
        }

        if (Notices is null || Notices.AgingFrom is < 0 or > AgingRules.MaxLevel || Notices.LevelFrom < 0 || Notices.LevelEvery < 1)
        {
            throw new InvalidOperationException($"The aging notice must start at 0 to {AgingRules.MaxLevel}, the level notice at 0 or more, every 1 level or more.");
        }

        if (Arena is null || Arena.Hours >> 24 != 0 || Arena.FirstPrize < 0 || Arena.SecondPrize < 0 || Arena.ThirdPrize < 0)
        {
            throw new InvalidOperationException("The arena's hours must be 0 to 23 and its prizes not negative.");
        }

        if (Royale is null || Royale.Hours >> 24 != 0 || Royale.FirstPrize < 0 || Royale.SecondPrize < 0 || Royale.ThirdPrize < 0)
        {
            throw new InvalidOperationException("The Battle Royale's hours must be 0 to 23 and its prizes not negative.");
        }

        if ((Royale.Hours & Arena.Hours) != 0)
        {
            // Both run in the quest arena's field: the arena's half hour and the event's quarter would share it.
            throw new InvalidOperationException("The Battle Royale and the arena must not share an hour.");
        }

        if (PvpDamagePercent is < 1 or > Simulation.Combat.PvpRules.MaxDamagePercent)
        {
            throw new InvalidOperationException($"World:PvpDamagePercent must be 1 to {Simulation.Combat.PvpRules.MaxDamagePercent}.");
        }

        return this;
    }
}

// The server's notices of other players' feats (ServerNoticeRules): agings from AgingFrom, levels that are multiples of
// LevelEvery from LevelFrom; 0 turns either off.
public sealed record ServerNoticeOptions(int AgingFrom = Simulation.Social.ServerNoticeRules.DefaultAgingFrom,
    int LevelFrom = Simulation.Social.ServerNoticeRules.DefaultLevelFrom, int LevelEvery = Simulation.Social.ServerNoticeRules.DefaultLevelEvery)
{
    public static readonly ServerNoticeOptions Default = new();
}

// The arena (ArenaRules): the server-local hours it opens at (EventSchedule.HourMask; 0 never opens) and the gold the
// week's first, second and third of each level band receive by mail (0 sends nothing).
public sealed record ArenaOptions(int Hours, long FirstPrize = ArenaOptions.DefaultFirstPrize, long SecondPrize = ArenaOptions.DefaultSecondPrize,
    long ThirdPrize = ArenaOptions.DefaultThirdPrize)
{
    public const long DefaultFirstPrize = 10_000_000;
    public const long DefaultSecondPrize = 5_000_000;
    public const long DefaultThirdPrize = 2_000_000;

    public static readonly ArenaOptions Default = new(Simulation.World.Calendar.EventSchedule.ParseHours(Simulation.Combat.ArenaRules.DefaultHours) ?? 0);

    public long PrizeFor(int place) => place switch
    {
        1 => FirstPrize,
        2 => SecondPrize,
        3 => ThirdPrize,
        _ => 0
    };
}

// The Battle Royale (RoyaleRules): the server-local hours it starts at (EventSchedule.HourMask; 0 never) and the gold
// the event's first, second and third receive by mail (0 sends nothing).
public sealed record RoyaleOptions(int Hours, long FirstPrize = RoyaleOptions.DefaultFirstPrize, long SecondPrize = RoyaleOptions.DefaultSecondPrize,
    long ThirdPrize = RoyaleOptions.DefaultThirdPrize)
{
    public const long DefaultFirstPrize = 5_000_000;
    public const long DefaultSecondPrize = 2_000_000;
    public const long DefaultThirdPrize = 1_000_000;

    public static readonly RoyaleOptions Default = new(Simulation.World.Calendar.EventSchedule.ParseHours(Simulation.Combat.RoyaleRules.DefaultHours) ?? 0);

    public long PrizeFor(int place) => place switch
    {
        1 => FirstPrize,
        2 => SecondPrize,
        3 => ThirdPrize,
        _ => 0
    };
}

// Bellatra: when it opens, the percent of each prize kept for the operating clan, and the divisor from a monster's
// experience to its score.
public sealed record BellatraOptions(Simulation.World.Bellatra.BellatraMode Mode = Simulation.World.Bellatra.BellatraMode.Hours,
    int Tax = Simulation.World.Bellatra.BellatraRules.DefaultTax, int ScoreDivisor = Simulation.World.Bellatra.BellatraRules.DefaultScoreDivisor)
{
    public static readonly BellatraOptions Default = new();
}

// The authoritative command queue refused a command; the caller drops it like a lost packet.
public sealed class WorldBusyException(string message) : InvalidOperationException(message);

// A commit found the character or a reservation changed after its operation was stored: storage is ahead of memory, so
// the session must reload from it. An error, never a refusal.
public sealed class CommitInvariantException(string message) : Exception(message);
