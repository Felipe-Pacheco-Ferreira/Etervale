using System.Numerics;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Combat;

// The arena's modes: one against one, or a party of three against another.
public enum ArenaMode : byte
{
    Solo = 1,
    Trio = 3
}

// How a match ended for a side.
public enum ArenaOutcome : byte
{
    Won,
    Lost,
    Draw
}

// Why a registration was refused.
public enum ArenaJoin : byte
{
    Queued,
    Left,
    Closed,
    Level,
    Party,
    Busy,
    NotQueued
}

// Arena matches between players. The arena opens for OpenMinutes from each of the server's hours (arena.hours); while it
// is open, players of MinimumLevel or more queue alone (Solo) or as a party of exactly three (Trio), and every
// PairEveryMs the queue pairs the oldest two teams of one mode and one level band (Band: 100-119, 120-139, 140 and up;
// a team's band is its highest member's). A match runs MatchMs in the quest arena's field, each match seeing only its own
// players: a blow never takes the last life (DuelRules.Blow) and the one it would takes the player out of the match.
// The side with nobody left loses; at the time limit the side that dealt more damage wins, equal damage is a draw. A win
// gives WinPoints of the week's arena points, a loss takes LossPoints down to 0 (stored by the arena's repository); the week starts on Monday at
// 00:00 server time (WeekStart). Blows take the PvP mode's damage percent, and nothing is lost.
public static class ArenaRules
{
    public const int MinimumLevel = 100;
    public const int Bands = 3;
    public const int OpenMinutes = 30;
    public const float PairEveryMs = 60_000f;
    public const float MatchMs = 300_000f;
    public const int WinPoints = 20;
    public const int LossPoints = 10;
    public const int FieldCode = QuestArena.FieldCode;
    public const int PrizePlaces = 3;
    public const string DefaultHours = "13,19,22";

    // The two sides' places in the arena: one where the quest arena takes a player in, the other where its opponent stands.
    public static readonly Vector3 FirstSide = new(QuestArena.EntryX, 0f, QuestArena.EntryZ);
    public static readonly Vector3 SecondSide = new(QuestArena.OpponentX, 0f, QuestArena.OpponentZ);

    // The members of a side stand this far apart across its line.
    public const float MemberSpacing = 40f;

    // The level band 0 to Bands - 1, or -1 below MinimumLevel.
    public static int Band(int level) => level < MinimumLevel ? -1 : level < 120 ? 0 : level < 140 ? 1 : 2;

    public static int TeamSize(ArenaMode mode) => (int)mode;

    // How players write a mode, in the chat command and on the HUD.
    public static string ModeName(ArenaMode mode) => mode == ArenaMode.Solo ? "1x1" : "3x3";

    public static ArenaMode? ModeOf(string text) =>
        string.Equals(text, ModeName(ArenaMode.Solo), StringComparison.OrdinalIgnoreCase) ? ArenaMode.Solo
        : string.Equals(text, ModeName(ArenaMode.Trio), StringComparison.OrdinalIgnoreCase) ? ArenaMode.Trio
        : null;

    // Open at a server-local time when its hour is one of the arena's hours (EventSchedule.HourMask) and within OpenMinutes.
    public static bool IsOpen(int hourMask, DateTimeOffset local) => (hourMask & 1 << local.Hour) != 0 && local.Minute < OpenMinutes;

    // Where a side's member stands: the side's place, members spread along the line between the two sides' places.
    public static Vector3 Place(int side, int member)
    {
        var center = side == 0 ? FirstSide : SecondSide;
        var across = Vector3.Normalize(new Vector3(SecondSide.Z - FirstSide.Z, 0f, FirstSide.X - SecondSide.X));
        return center + across * ((member - 1) * MemberSpacing);
    }

    // The first side's outcome: a side with nobody left loses; at the limit, more damage wins and equal damage draws.
    public static ArenaOutcome? Outcome(int firstStanding, int secondStanding, long firstDamage, long secondDamage, bool timeUp)
    {
        if (firstStanding == 0 || secondStanding == 0)
        {
            return firstStanding == secondStanding ? ArenaOutcome.Draw : firstStanding > 0 ? ArenaOutcome.Won : ArenaOutcome.Lost;
        }

        if (!timeUp)
        {
            return null;
        }

        return firstDamage == secondDamage ? ArenaOutcome.Draw : firstDamage > secondDamage ? ArenaOutcome.Won : ArenaOutcome.Lost;
    }

    public static ArenaOutcome Other(ArenaOutcome outcome) => outcome switch
    {
        ArenaOutcome.Won => ArenaOutcome.Lost,
        ArenaOutcome.Lost => ArenaOutcome.Won,
        _ => ArenaOutcome.Draw
    };

    // The Monday the server-local day's week starts on, and the week as stored (yyyymmdd).
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static int WeekKey(DateOnly week) => week.Year * 10_000 + week.Month * 100 + week.Day;

    // A band's level range as players read it.
    public static string BandName(int band) => band switch
    {
        0 => "100-119",
        1 => "120-139",
        2 => "140+",
        _ => ""
    };
}
