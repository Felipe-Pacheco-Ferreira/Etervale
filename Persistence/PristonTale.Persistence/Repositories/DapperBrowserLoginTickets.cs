using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperBrowserLoginTickets(IDbConnectionFactory connections) : IBrowserLoginTickets
{
    public async Task IssueAsync(string hash, long accountId, string passwordStamp, long now, long expires, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM browser_login_tickets WHERE expires_unix <= @now", new { now }, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO browser_login_tickets (ticket_hash, account_id, password_stamp, expires_unix)
            VALUES (@hash, @accountId, @passwordStamp, @expires)
            """, new { hash, accountId, passwordStamp, expires }, cancellationToken: ct));
    }

    public async Task<bool> RedeemAsync(string hash, long accountId, string passwordStamp, long now, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM browser_login_tickets
            WHERE ticket_hash = @hash AND account_id = @accountId AND password_stamp = @passwordStamp AND expires_unix > @now
            """, new { hash, accountId, passwordStamp, now }, cancellationToken: ct)) == 1;
    }
}
