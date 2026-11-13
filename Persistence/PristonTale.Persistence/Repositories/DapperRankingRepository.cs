using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Rankings;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperRankingRepository(IDbConnectionFactory connectionFactory) : IRankingRepository
{
    private const string TopSql =
        """
        SELECT c.name, c.job_code, c.tier, c.level, c.exp, k.name AS clan_name
        FROM characters c
        JOIN accounts a ON a.id = c.account_id
        LEFT JOIN clan_members m ON m.character_id = c.id
        LEFT JOIN clans k ON k.id = m.clan_id
        WHERE a.gm_level = 0 AND NOT a.is_blocked
        """;

    private const string OrderSql = " ORDER BY c.level DESC, c.exp DESC, c.id LIMIT @limit";

    public async Task<IReadOnlyList<RankedCharacter>> TopAsync(int? jobCode, int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = jobCode is { } job
            ? await connection.QueryAsync<RankedCharacter>(new CommandDefinition(
                TopSql + " AND c.job_code = @job" + OrderSql, new { job, limit }, cancellationToken: ct))
            : await connection.QueryAsync<RankedCharacter>(new CommandDefinition(TopSql + OrderSql, new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<WorldTotals> TotalsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleAsync<WorldTotals>(new CommandDefinition(
            "SELECT (SELECT COUNT(*) FROM accounts) AS accounts, (SELECT COUNT(*) FROM characters) AS characters",
            cancellationToken: ct));
    }
}
