using System.Numerics;
using PristonTale.Simulation.Combat;

namespace PristonTale.Application.World;

// A running duel (DuelRules) between two players of one field: their names, the arena's center, how long it ran and how
// long each side has stood out of the arena in a row, and what each side's HUD shows (rebuilt only when a shown second
// changes). The tick alone changes it.
public sealed class Duel
{
    private readonly float[] _outsideMs = new float[2];
    private readonly DuelView[] _views = new DuelView[2];

    public Duel(WorldPlayer first, WorldPlayer second, int fieldId)
    {
        Ids = [first.EntityId, second.EntityId];
        Names = [first.Character.Name, second.Character.Name];
        FieldId = fieldId;
        Center = DuelRules.Center(first.Position, second.Position);
        Refresh();
    }

    public ulong[] Ids { get; }
    public string[] Names { get; }
    public int FieldId { get; }
    public Vector3 Center { get; }
    public float ElapsedMs { get; private set; }

    public int SideOf(ulong entityId) => entityId == Ids[0] ? 0 : 1;

    public float OutsideMs(int side) => _outsideMs[side];

    public DuelView ViewOf(ulong entityId) => _views[SideOf(entityId)];

    // One step: the time run, and each side in or out of the arena at its position.
    internal void Step(float dtMs, Vector3 first, Vector3 second)
    {
        ElapsedMs += dtMs;
        _outsideMs[0] = DuelRules.InArena(Center, first) ? 0f : _outsideMs[0] + dtMs;
        _outsideMs[1] = DuelRules.InArena(Center, second) ? 0f : _outsideMs[1] + dtMs;
        Refresh();
    }

    private void Refresh()
    {
        var left = DuelRules.SecondsLeft(DuelRules.LimitMs - ElapsedMs);
        for (var side = 0; side < 2; side++)
        {
            var outside = _outsideMs[side] > 0f ? DuelRules.SecondsLeft(DuelRules.OutsideMs - _outsideMs[side]) : 0;
            if (_views[side] is not { } view || view.SecondsLeft != left || view.OutsideSecondsLeft != outside)
            {
                _views[side] = new DuelView(Ids[1 - side], Names[1 - side], Center, left, outside);
            }
        }
    }
}

// A challenge waiting for the target's answer.
public sealed record DuelRequest(ulong FromId, string FromName, long ExpiresMs);

// What a duellist's HUD shows: the opponent, the arena's center, the whole seconds left and, while it stands out of the
// arena, the whole seconds before it loses (0 inside).
public sealed record DuelView(ulong OpponentId, string OpponentName, Vector3 Center, int SecondsLeft, int OutsideSecondsLeft);
