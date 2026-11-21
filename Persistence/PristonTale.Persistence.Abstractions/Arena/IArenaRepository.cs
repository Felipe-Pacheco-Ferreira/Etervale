namespace PristonTale.Persistence.Abstractions.Arena;

// A character's week in the arena (ArenaRules), for the rankings.
public sealed record ArenaStanding(long CharacterId, long AccountId, string Name, int JobCode, int Level, int Points, int Wins, int Losses);

// A prize the week's settlement mailed: its band and place, the character and the gold.
public sealed record ArenaPrize(int Band, int Place, string Name, long Gold);

// The arena's weekly points (arena_scores) and prizes (arena_prizes). A week is its Monday, server-local, as yyyymmdd.
public interface IArenaRepository
{
    // A result: a win adds winPoints, a loss takes lossPoints down to 0, with the count of each.
    Task RecordAsync(int week, int band, long characterId, bool won, int winPoints, int lossPoints, CancellationToken ct = default);

    // A band's week, by points, then wins, then fewer losses; game masters' and blocked accounts' characters left out.
    Task<IReadOnlyList<ArenaStanding>> TopAsync(int week, int band, int count, CancellationToken ct = default);

    // Pays a band's week once: the best with points, one per place, each mailed its gold (none for 0) with this subject
    // and body; nothing when the week's band was already paid. The prizes it paid.
    Task<IReadOnlyList<ArenaPrize>> AwardAsync(int week, int band, IReadOnlyList<long> gold, string subject, string body, DateTime nowUtc,
        CancellationToken ct = default);
}
