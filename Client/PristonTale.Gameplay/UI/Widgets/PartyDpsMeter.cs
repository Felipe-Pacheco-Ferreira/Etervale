namespace PristonTale.Gameplay.UI.Widgets;

// The damage per second the local player and its party deal to a target (ClientSettings.ShowPartyDps): the hits of the
// last WindowMs over the time since the first of them, at least a second, so a single hit does not read as a burst.
public sealed class PartyDpsMeter
{
    public const long WindowMs = 5000;
    private const long ShortestSpanMs = 1000;
    private readonly Queue<(long AtMs, ulong Target, int Damage)> _hits = new();

    public void Record(ulong target, int damage, long nowMs)
    {
        if (target != 0 && damage > 0)
        {
            _hits.Enqueue((nowMs, target, damage));
        }

        Prune(nowMs);
    }

    public int Dps(ulong target, long nowMs)
    {
        Prune(nowMs);
        long total = 0;
        var first = long.MaxValue;
        foreach (var (atMs, hitTarget, damage) in _hits)
        {
            if (hitTarget == target)
            {
                total += damage;
                first = Math.Min(first, atMs);
            }
        }

        return total == 0 ? 0 : (int)(total * 1000 / Math.Max(ShortestSpanMs, nowMs - first));
    }

    private void Prune(long nowMs)
    {
        while (_hits.Count > 0 && nowMs - _hits.Peek().AtMs > WindowMs)
        {
            _hits.Dequeue();
        }
    }
}
