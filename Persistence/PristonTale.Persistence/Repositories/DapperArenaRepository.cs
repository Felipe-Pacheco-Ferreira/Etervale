using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Arena;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperArenaRepository(IDbConnectionFactory connectionFactory) : IArenaRepository
{
    private const string TopSql =
        """
        SELECT s.character_id AS CharacterId, c.account_id AS AccountId, c.name AS Name, c.job_code AS JobCode, c.level AS Level,
               s.points AS Points, s.wins AS Wins, s.losses AS Losses
        FROM arena_scores s
        JOIN characters c ON c.id = s.character_id
        JOIN accounts a ON a.id = c.account_id
        WHERE s.week = @Week AND s.band = @Band AND a.gm_level = 0 AND NOT a.is_blocked
        ORDER BY s.points DESC, s.wins DESC, s.losses, s.character_id LIMIT @Count
        """;

    public async Task RecordAsync(int week, int band, long characterId, bool won, int winPoints, int lossPoints, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO arena_scores (week, band, character_id, points, wins, losses)
            VALUES (@Week, @Band, @CharacterId, @First, @Win, 1 - @Win)
            ON CONFLICT (week, band, character_id) DO UPDATE SET
                points = CASE WHEN @Win = 1 THEN arena_scores.points + @WinPoints
                              WHEN arena_scores.points > @LossPoints THEN arena_scores.points - @LossPoints ELSE 0 END,
                wins = arena_scores.wins + @Win,
                losses = arena_scores.losses + 1 - @Win
            """,
            new { Week = week, Band = band, CharacterId = characterId, Win = won ? 1 : 0, First = won ? winPoints : 0, WinPoints = winPoints, LossPoints = lossPoints },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ArenaStanding>> TopAsync(int week, int band, int count, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ArenaStanding>(new CommandDefinition(TopSql, new { Week = week, Band = band, Count = count }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ArenaPrize>> AwardAsync(int week, int band, IReadOnlyList<long> gold, string subject, string body, DateTime nowUtc,
        CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var paid = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM arena_prizes WHERE week = @Week AND band = @Band", new { Week = week, Band = band }, transaction, cancellationToken: ct));
        if (paid > 0 || gold.Count == 0)
        {
            return [];
        }

        var best = (await connection.QueryAsync<ArenaStanding>(new CommandDefinition(TopSql, new { Week = week, Band = band, Count = gold.Count },
            transaction, cancellationToken: ct))).Where(static standing => standing.Points > 0).ToList();
        var prizes = new List<ArenaPrize>();
        for (var index = 0; index < best.Count; index++)
        {
            var standing = best[index];
            var place = index + 1;
            long? mailId = null;
            if (gold[index] > 0)
            {
                mailId = await GameMailPrizes.SendGoldAsync(connection, transaction, standing.AccountId, subject, body, gold[index], nowUtc, ct);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO arena_prizes (week, band, place, character_id, gold, mail_id) VALUES (@Week, @Band, @Place, @CharacterId, @Gold, @MailId)",
                new { Week = week, Band = band, Place = place, standing.CharacterId, Gold = gold[index], MailId = mailId }, transaction, cancellationToken: ct));
            prizes.Add(new ArenaPrize(band, place, standing.Name, gold[index]));
        }

        if (prizes.Count == 0)
        {
            // Nobody scored: the week's band is still marked paid, with no prize row.
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO arena_prizes (week, band, place, character_id, gold, mail_id) VALUES (@Week, @Band, 0, 0, 0, NULL)",
                new { Week = week, Band = band }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return prizes;
    }
}
