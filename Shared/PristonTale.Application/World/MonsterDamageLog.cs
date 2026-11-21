namespace PristonTale.Application.World;

internal sealed class MonsterDamageLog
{
    public ulong KillerId { get; private set; }

    public ulong FirstAttackerId { get; private set; }

    public int FirstAttackerDamage { get; private set; }

    public long FirstAttackerLastHitMs { get; private set; }

    // The life each player took, kept only for a scheduled boss (the boss roulette, Tally); null otherwise.
    public Dictionary<ulong, long>? ByPlayer { get; private set; }

    public void RecordHit(ulong attackerId, int damage, bool monsterAtFullLifeBeforeHit, long nowMs)
    {
        if (attackerId == 0 || damage <= 0)
            return;

        KillerId = attackerId;

        if (FirstAttackerId == 0 && monsterAtFullLifeBeforeHit)
            FirstAttackerId = attackerId;

        if (attackerId == FirstAttackerId)
        {
            FirstAttackerDamage += damage;
            FirstAttackerLastHitMs = nowMs;
        }
    }

    // The life a player took from a scheduled boss (only what the boss had left, not the blow's overkill).
    public void Tally(ulong attackerId, int dealt)
    {
        ByPlayer ??= new Dictionary<ulong, long>();
        ByPlayer[attackerId] = ByPlayer.GetValueOrDefault(attackerId) + dealt;
    }
}
