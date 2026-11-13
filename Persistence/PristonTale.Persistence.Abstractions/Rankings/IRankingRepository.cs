namespace PristonTale.Persistence.Abstractions.Rankings;

// A character of the public rankings, with its clan's name when it has one.
public sealed record RankedCharacter(string Name, int JobCode, short Tier, int Level, long Exp, string? ClanName);

public sealed record WorldTotals(long Accounts, long Characters);

// Read-only views of the players for the public website. Characters of game masters and of blocked accounts are left out.
public interface IRankingRepository
{
    // The highest characters by level, then experience, then age; of one class (its job code) or of all when null.
    Task<IReadOnlyList<RankedCharacter>> TopAsync(int? jobCode, int limit, CancellationToken ct = default);

    Task<WorldTotals> TotalsAsync(CancellationToken ct = default);
}
