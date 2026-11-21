using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.BossRoulette;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperBossRouletteRepository(IDbConnectionFactory connectionFactory) : IBossRouletteRepository
{
    public async Task<long?> RecordAsync(BossRouletteRecord draw, string subject, string body, DateTime nowUtc, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var account = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT account_id FROM characters WHERE id = @Id", new { Id = draw.WinnerCharacterId }, transaction, cancellationToken: ct));
        long? mailId = account is { } accountId
            ? await GameMailPrizes.SendItemAsync(connection, transaction, accountId, subject, body, draw.PrizeKey, 1, nowUtc, ct)
            : null;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO boss_roulette (drawn_at, field_id, boss_code, boss_name, winner_character_id, prize_key, mail_id, entrants)
            VALUES (@Now, @FieldId, @BossCode, @BossName, @WinnerCharacterId, @PrizeKey, @MailId, @Entrants)
            """,
            new { Now = nowUtc, draw.FieldId, draw.BossCode, draw.BossName, draw.WinnerCharacterId, draw.PrizeKey, MailId = mailId, draw.Entrants },
            transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return mailId;
    }
}
