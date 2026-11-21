using System.Numerics;
using PristonTale.Simulation.Combat;

namespace PristonTale.Application.World;

// A registration for the next Battle Royale: the field and place the player registered from, to go back to.
public sealed record RoyaleEntry(int FieldId, Vector3 Position);

// A player's place in a running event: who it is, where it goes back to, the damage it dealt (the field's step adds it)
// and, once out, the order it went out in (1 for the first).
public sealed class RoyaleSeat(RoyaleEvent royale, int index, ulong entityId, string name, long characterId, RoyaleEntry origin)
{
    public RoyaleEvent Event { get; } = royale;
    public int Index { get; } = index;
    public ulong EntityId { get; } = entityId;
    public string Name { get; } = name;
    public long CharacterId { get; } = characterId;
    public RoyaleEntry Origin { get; } = origin;
    public long Damage { get; set; }
    public int OutOrder { get; private set; }
    public bool Out => OutOrder > 0;

    internal void TakeOut(int order) => OutOrder = order;
}

// What a player's HUD shows of the Battle Royale: registered (with how many are), or in the event with the players
// standing of all who started, the zone's radius, the whole seconds until it shrinks again (0 when it no longer does) and
// until the time limit, and whether this player is out.
public sealed record RoyaleView(bool Registered, int Standing, int Total, int Radius, int SecondsToShrink, int SecondsLeft, bool Out);

// One player's place at the end and the experience it gained; gold for the first RoyaleRules.PrizePlaces.
public sealed record RoyalePlace(int Place, long CharacterId, string Name, ulong Experience);

// A running Battle Royale (RoyaleRules) on the tick thread: its seats in the order they started, how long it ran, the
// zone's steps, and the view each seat's HUD shows, made again only when it changed.
public sealed class RoyaleEvent
{
    private readonly RoyaleView?[] _views;
    private int _outCount;

    public RoyaleEvent(long id, long key)
    {
        Id = id;
        Key = key;
        _views = new RoyaleView?[RoyaleRules.MaximumPlayers];
    }

    public long Id { get; }

    // The event's key as stored (RoyaleRules.EventKey).
    public long Key { get; }

    public List<RoyaleSeat> Seats { get; } = [];

    // The potions it laid in the arena's field still lying there: only its players standing take them.
    public HashSet<Guid> Potions { get; } = [];
    public float ElapsedMs { get; private set; }
    public int Steps => RoyaleRules.StepsAfter(ElapsedMs);
    public float Radius => RoyaleRules.Radius(Steps);
    public float PotionMsLeft { get; set; } = RoyaleRules.PotionEveryMs;
    public float ZoneMsLeft { get; set; } = RoyaleRules.ZoneDamageEveryMs;

    public int Standing => Seats.Count - _outCount;

    public void Step(float dtMs) => ElapsedMs += dtMs;

    public void TakeOut(RoyaleSeat seat)
    {
        if (!seat.Out)
        {
            seat.TakeOut(++_outCount);
        }
    }

    // The places: those standing by the damage they dealt, then those out, the last out first.
    public List<RoyaleSeat> Ranking()
    {
        var ranking = new List<RoyaleSeat>(Seats);
        ranking.Sort(static (a, b) => a.Out != b.Out ? a.Out.CompareTo(b.Out)
            : a.Out ? b.OutOrder.CompareTo(a.OutOrder)
            : b.Damage.CompareTo(a.Damage));
        return ranking;
    }

    public RoyaleView ViewOf(RoyaleSeat seat)
    {
        var shrink = Steps >= RoyaleRules.ZoneSteps ? 0 : (int)MathF.Ceiling(((Steps + 1) * RoyaleRules.ZoneStepMs - ElapsedMs) / 1000f);
        var left = (int)MathF.Ceiling(MathF.Max(0f, RoyaleRules.LimitMs - ElapsedMs) / 1000f);
        var radius = (int)Radius;
        if (_views[seat.Index] is { } view && view.Standing == Standing && view.Radius == radius && view.SecondsToShrink == shrink && view.SecondsLeft == left
            && view.Out == seat.Out)
        {
            return view;
        }

        return _views[seat.Index] = new RoyaleView(false, Standing, Seats.Count, radius, shrink, left, seat.Out);
    }
}
