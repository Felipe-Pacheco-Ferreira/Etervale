using System.Data.Common;
using Dapper;

namespace PristonTale.Persistence.Repositories;

// An event's gold prize in the game's own mail, inside the caller's transaction: the player's account sends it to
// itself, as no system account exists. The mail's id.
internal static class GameMailPrizes
{
    public static Task<long> SendGoldAsync(DbConnection connection, DbTransaction transaction, long accountId, string subject, string body, long gold,
        DateTime nowUtc, CancellationToken ct) =>
        connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO game_mail (sender_account_id, recipient_account_id, subject, body, item_key, item_count, gold, created_at)
            VALUES (@Account, @Account, @Subject, @Body, NULL, 0, @Gold, @Now) RETURNING id
            """, new { Account = accountId, Subject = subject, Body = body, Gold = gold, Now = nowUtc }, transaction, cancellationToken: ct));

    // A prize of items (a catalog key and count) in the game's mail, the same way: the mail's id.
    public static async Task<long> SendItemAsync(DbConnection connection, DbTransaction transaction, long accountId, string subject, string body,
        string itemKey, int count, DateTime nowUtc, CancellationToken ct)
    {
        var mailId = await SendGoldAsync(connection, transaction, accountId, subject, body, 0, nowUtc, ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO game_mail_items (mail_id, item_key, quantity) VALUES (@Mail, @Key, @Count)",
            new { Mail = mailId, Key = itemKey, Count = count }, transaction, cancellationToken: ct));
        return mailId;
    }
}
