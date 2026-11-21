using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items.Aging;

namespace PristonTale.Simulation.Titles;

// The titles a player may show over its name (TitleRules.All). Permanent titles are earned once and kept; a position
// title belongs to one character at a time and passes to whoever takes the position. Values are stored and sent.
public enum TitleCode : byte
{
    None = 0,
    Veteran = 1,
    Master = 2,
    Legend = 3,
    Immortal = 4,
    Artisan = 5,
    MasterArtisan = 6,
    Adventurer = 7,
    Hunter = 8,
    Exterminator = 9,
    BossHunter = 10,
    ArenaKingLow = 11,
    ArenaKingMiddle = 12,
    ArenaKingHigh = 13,
    Survivor = 14,
    CastleLord = 15,
    Strongest = 16
}

// How rare a title is: the colour of its line over the name, and whether every player hears that it was earned.
public enum TitleRarity : byte
{
    Common,
    Rare,
    Epic,
    Legendary
}

// What earns a permanent title (its Goal: a level, an aging level, a count of kills), or a position held.
public enum TitleGoal : byte
{
    Level,
    Aging,
    Journey,
    MonsterKills,
    BossKills,
    Position
}

public readonly record struct TitleDefinition(TitleCode Code, TitleRarity Rarity, TitleGoal Goal, long Target)
{
    public bool IsPosition => Goal == TitleGoal.Position;
}

// The titles: only a look, never an attribute. A permanent title is earned when its goal is met (TitleBook.Observe); a
// position title is handed by the world to the position's holder: the arena's first of the week before in each level
// band (ArenaRules.Band), the last Battle Royale's winner, the chief of the clan that owns the castle and the first of
// the level ranking.
public static class TitleRules
{
    public const int Count = (int)TitleCode.Strongest + 1;

    public static readonly TitleDefinition[] All =
    [
        new(TitleCode.Veteran, TitleRarity.Common, TitleGoal.Level, 100),
        new(TitleCode.Master, TitleRarity.Rare, TitleGoal.Level, 120),
        new(TitleCode.Legend, TitleRarity.Epic, TitleGoal.Level, 140),
        new(TitleCode.Immortal, TitleRarity.Legendary, TitleGoal.Level, 160),
        new(TitleCode.Artisan, TitleRarity.Rare, TitleGoal.Aging, 14),
        new(TitleCode.MasterArtisan, TitleRarity.Legendary, TitleGoal.Aging, AgingRules.MaxLevel),
        new(TitleCode.Adventurer, TitleRarity.Common, TitleGoal.Journey, 1),
        new(TitleCode.Hunter, TitleRarity.Common, TitleGoal.MonsterKills, 1_000),
        new(TitleCode.Exterminator, TitleRarity.Epic, TitleGoal.MonsterKills, 100_000),
        new(TitleCode.BossHunter, TitleRarity.Rare, TitleGoal.BossKills, 100),
        new(TitleCode.ArenaKingLow, TitleRarity.Legendary, TitleGoal.Position, 0),
        new(TitleCode.ArenaKingMiddle, TitleRarity.Legendary, TitleGoal.Position, 0),
        new(TitleCode.ArenaKingHigh, TitleRarity.Legendary, TitleGoal.Position, 0),
        new(TitleCode.Survivor, TitleRarity.Legendary, TitleGoal.Position, 0),
        new(TitleCode.CastleLord, TitleRarity.Legendary, TitleGoal.Position, 0),
        new(TitleCode.Strongest, TitleRarity.Legendary, TitleGoal.Position, 0)
    ];

    // How often the world looks again at who holds the positions read from storage (the arena, the castle, the ranking).
    public static readonly TimeSpan PositionCheck = TimeSpan.FromMinutes(5);

    public static bool IsTitle(int code) => code is > (int)TitleCode.None and < Count;

    public static TitleDefinition Of(TitleCode code) => All[(int)code - 1];

    public static bool IsPosition(TitleCode code) => IsTitle((int)code) && Of(code).IsPosition;

    // A permanent title's bit in TitleBook.Earned and in the owned mask the owner is sent.
    public static uint Bit(TitleCode code) => 1u << (int)code;

    // The bits of every permanent title.
    public static readonly uint PermanentBits = All.Where(static title => !title.IsPosition).Aggregate(0u, static (bits, title) => bits | Bit(title.Code));

    // The arena's title for a level band (0 to ArenaRules.Bands - 1).
    public static TitleCode ArenaKing(int band) => band is >= 0 and < ArenaRules.Bands
        ? (TitleCode)((int)TitleCode.ArenaKingLow + band)
        : throw new ArgumentOutOfRangeException(nameof(band));

    // Every player hears that a title was earned or changed hands only for a legendary one; the others are told to their
    // owner alone.
    public static bool Announced(TitleCode code) => Of(code).Rarity == TitleRarity.Legendary;
}
