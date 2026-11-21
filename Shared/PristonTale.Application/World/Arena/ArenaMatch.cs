using System.Numerics;
using PristonTale.Simulation.Combat;

namespace PristonTale.Application.World;

// A player's place in a match (ArenaRules): its side (0 or 1) and its index in the side.
public sealed record ArenaSeat(ArenaMatch Match, int Side, int Member)
{
    public bool Out => Match.IsOut(Side, Member);

    public bool FoeOf(ArenaSeat other) => ReferenceEquals(Match, other.Match) && Side != other.Side && !Out && !other.Out;
}

// A team waiting in the arena's queue: its mode, level band, members (the leader first), the field and place each member
// queued from (to go back to after the match) and when it queued.
public sealed record ArenaQueueEntry(ArenaMode Mode, int Band, ulong[] Members, (int FieldId, Vector3 Position)[] Origins, long QueuedAtMs);

// What a player's HUD shows of the arena: queued for a mode, or in a match with its side's and the other side's players
// still standing, the whole seconds left and whether this player is out.
public sealed record ArenaView(ArenaMode Mode, bool Queued, int OwnStanding, int FoeStanding, int SecondsLeft, bool Out);

// A running match: its two sides (entity ids, names, the field and place each member queued from, to go back to, and the
// stored characters' ids), which members are out, the damage each side dealt and how long it ran. The tick changes it;
// the arena field's step adds damage (the field steps alone in its thread, before the tick reads it).
public sealed class ArenaMatch
{
    private readonly bool[][] _out;
    private readonly ArenaView?[][] _views;

    public ArenaMatch(long id, ArenaMode mode, int band, ulong[][] sides, string[][] names, (int FieldId, Vector3 Position)[][] origins, long[][] characterIds)
    {
        Id = id;
        Mode = mode;
        Band = band;
        Sides = sides;
        Names = names;
        Origins = origins;
        CharacterIds = characterIds;
        _out = [new bool[sides[0].Length], new bool[sides[1].Length]];
        _views = [new ArenaView?[sides[0].Length], new ArenaView?[sides[1].Length]];
    }

    public long Id { get; }
    public ArenaMode Mode { get; }
    public int Band { get; }
    public ulong[][] Sides { get; }
    public string[][] Names { get; }
    public (int FieldId, Vector3 Position)[][] Origins { get; }
    public long[][] CharacterIds { get; }
    public long[] Damage { get; } = new long[2];
    public float ElapsedMs { get; private set; }

    public bool IsOut(int side, int member) => _out[side][member];

    public int Standing(int side)
    {
        var standing = 0;
        foreach (var gone in _out[side])
        {
            if (!gone)
            {
                standing++;
            }
        }

        return standing;
    }

    internal void TakeOut(int side, int member) => _out[side][member] = true;

    internal void Step(float dtMs) => ElapsedMs += dtMs;

    // A member's HUD view, rebuilt only when what it shows changed.
    public ArenaView ViewOf(int side, int member)
    {
        var seconds = DuelRules.SecondsLeft(ArenaRules.MatchMs - ElapsedMs);
        var own = Standing(side);
        var foe = Standing(1 - side);
        var outOf = _out[side][member];
        if (_views[side][member] is not { } view || view.SecondsLeft != seconds || view.OwnStanding != own || view.FoeStanding != foe || view.Out != outOf)
        {
            view = new ArenaView(Mode, false, own, foe, seconds, outOf);
            _views[side][member] = view;
        }

        return view;
    }
}
