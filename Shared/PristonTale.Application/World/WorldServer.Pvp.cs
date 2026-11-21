namespace PristonTale.Application.World;

// The PvP mode across a leave (PvpRules): a player who leaves holding its switch on (a recent blow) or marked a murderer
// enters again, while the server runs, with what is left of both on the real clock and its PvP on, so leaving never
// clears them. Tick thread only.
public sealed partial class WorldServer
{
    private readonly record struct PvpCarry(float HoldMs, float MurdererMs, long LeftAtMs);

    private readonly Dictionary<string, PvpCarry> _pvpCarried = new(StringComparer.OrdinalIgnoreCase);

    private void KeepPvp(WorldPlayer player)
    {
        var now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var (name, carry) in _pvpCarried.ToArray())
        {
            if (Left(carry.HoldMs, carry, now) <= 0f && Left(carry.MurdererMs, carry, now) <= 0f)
            {
                _pvpCarried.Remove(name);
            }
        }

        if (player.Pvp && (player.PvpCombatMsLeft > 0f || player.Murderer))
        {
            _pvpCarried[player.Character.Name] = new PvpCarry(player.PvpCombatMsLeft, player.MurdererMsLeft, now);
        }
    }

    private void RestorePvp(WorldPlayer player)
    {
        if (_pvpCarried.Remove(player.Character.Name, out var carry))
        {
            var now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
            player.RestorePvp(Left(carry.HoldMs, carry, now), Left(carry.MurdererMs, carry, now));
        }
    }

    private static float Left(float ms, PvpCarry carry, long nowMs) => ms - (nowMs - carry.LeftAtMs);
}
