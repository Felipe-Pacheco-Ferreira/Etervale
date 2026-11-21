using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Royale;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperRoyaleRepository(IDbConnectionFactory connectionFactory) : IRoyaleRepository
{
    public async Task<IReadOnlyList<RoyalePrize>> AwardAsync(long eventKey, IReadOnlyList<RoyalePrize> prizes, string subject, string body, DateTime nowUtc,
        CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var paid = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM royale_prizes WHERE event_key = @Key", new { Key = eventKey }, transaction, cancellationToken: ct));
        if (paid > 0)
        {
            return [];
        }

        var done = new List<RoyalePrize>();
        foreach (var prize in prizes)
        {
            var account = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT account_id FROM characters WHERE id = @Id", new { Id = prize.CharacterId }, transaction, cancellationToken: ct));
            if (account is not { } accountId)
            {
                continue;
            }

            long? mailId = prize.Gold > 0 ? await GameMailPrizes.SendGoldAsync(connection, transaction, accountId, subject, body, prize.Gold, nowUtc, ct) : null;
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO royale_prizes (event_key, place, character_id, gold, mail_id) VALUES (@Key, @Place, @CharacterId, @Gold, @MailId)",
                new { Key = eventKey, prize.Place, prize.CharacterId, prize.Gold, MailId = mailId }, transaction, cancellationToken: ct));
            done.Add(prize);
        }

        if (done.Count == 0)
        {
            // Nobody to pay: the event is still marked paid, with no prize row.
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO royale_prizes (event_key, place, character_id, gold, mail_id) VALUES (@Key, 0, 0, 0, NULL)",
                new { Key = eventKey }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return done;
    }
}
