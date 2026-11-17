using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Repositories;

public sealed class GameMailClaims(IDbConnectionFactory database)
{
    public async Task<bool> StoreAsync(long mailId, CharacterRecord character)
    {
        await using var connection = await database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var claimed = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_mail_receipts (mail_id, account_id, read_at, claimed_at)
            SELECT id, @AccountId, @now, @now FROM game_mail
            WHERE id = @mailId AND (recipient_account_id IS NULL OR recipient_account_id = @AccountId)
            ON CONFLICT (mail_id, account_id) DO UPDATE SET claimed_at = EXCLUDED.claimed_at
            WHERE game_mail_receipts.claimed_at IS NULL
            """, new { mailId, character.AccountId, now = DateTime.UtcNow }, transaction));
        if (claimed != 1 || await connection.ExecuteAsync(new CommandDefinition(
            DapperCharacterRepository.UpdateSql, character, transaction)) != 1)
        {
            return false;
        }

        await transaction.CommitAsync();
        return true;
    }
}
