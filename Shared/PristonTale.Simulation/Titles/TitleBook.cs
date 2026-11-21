namespace PristonTale.Simulation.Titles;

// What a character's titles hold, as stored: the permanent titles earned (TitleRules.Bit), the title it shows, the
// monsters and bosses it killed and the highest aging level an item of its reached.
public sealed record TitleState(uint Earned, TitleCode Shown, long MonsterKills, long BossKills, int BestAging)
{
    public static readonly TitleState Empty = new(0, TitleCode.None, 0, 0, 0);
}

// A character's titles (TitleRules). The counts only grow; Revision moves when the earned titles or the shown one change,
// so the world sends the owner its titles again only then.
public sealed class TitleBook
{
    public uint Earned { get; private set; }
    public TitleCode Shown { get; private set; }
    public long MonsterKills { get; private set; }
    public long BossKills { get; private set; }
    public int BestAging { get; private set; }
    public long Revision { get; private set; }

    public TitleState Capture() => new(Earned, Shown, MonsterKills, BossKills, BestAging);

    public void Restore(TitleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if ((state.Earned & ~TitleRules.PermanentBits) != 0 || state.Shown != TitleCode.None && !TitleRules.IsTitle((int)state.Shown)
            || state.MonsterKills < 0 || state.BossKills < 0 || state.BestAging < 0)
        {
            throw new InvalidDataException("Saved titles are out of range.");
        }

        (Earned, Shown, MonsterKills, BossKills, BestAging) = (state.Earned, state.Shown, state.MonsterKills, state.BossKills, state.BestAging);
        Revision++;
    }

    public void CopyFrom(TitleBook other)
    {
        (Earned, Shown, MonsterKills, BossKills, BestAging, Revision) =
            (other.Earned, other.Shown, other.MonsterKills, other.BossKills, other.BestAging, other.Revision);
    }

    // A character change's copy hands back what only grows (the aging an item reached while it was prepared).
    public void Merge(TitleBook candidate) => BestAging = Math.Max(BestAging, candidate.BestAging);

    public bool Has(TitleCode code) => (Earned & TitleRules.Bit(code)) != 0;

    public void CountKill(bool boss)
    {
        MonsterKills++;
        if (boss)
        {
            BossKills++;
        }
    }

    public void RecordAging(int level) => BestAging = Math.Max(BestAging, level);

    // Marks earned every permanent title whose goal the character now meets; the titles it earned now (a bit each).
    public uint Observe(int level, bool journeyDone)
    {
        var earned = 0u;
        foreach (var title in TitleRules.All)
        {
            if (!title.IsPosition && !Has(title.Code) && Meets(title, level, journeyDone))
            {
                earned |= TitleRules.Bit(title.Code);
            }
        }

        if (earned != 0)
        {
            Earned |= earned;
            Revision++;
        }

        return earned;
    }

    private bool Meets(TitleDefinition title, int level, bool journeyDone) => title.Goal switch
    {
        TitleGoal.Level => level >= title.Target,
        TitleGoal.Aging => BestAging >= title.Target,
        TitleGoal.Journey => journeyDone,
        TitleGoal.MonsterKills => MonsterKills >= title.Target,
        TitleGoal.BossKills => BossKills >= title.Target,
        _ => false
    };

    // Shows a title the character owns (a permanent one earned, or a position it holds), or none; false for a title it
    // does not own.
    public bool Choose(TitleCode code, bool holdsPosition)
    {
        if (code != TitleCode.None && (!TitleRules.IsTitle((int)code) || (TitleRules.IsPosition(code) ? !holdsPosition : !Has(code))))
        {
            return false;
        }

        if (Shown != code)
        {
            Shown = code;
            Revision++;
        }

        return true;
    }
}
