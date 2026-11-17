using Dapper;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Server.Hosting;

internal sealed class GameMailReader(IDbConnectionFactory database)
{
    public async Task<IReadOnlyList<string>> ReadPageAsync(long accountId, long mailId, int page)
    {
        var message = await OpenAsync(accountId, mailId, CancellationToken.None);
        if (message is null)
        {
            return ["Correio: mensagem indisponível."];
        }

        var text = message.Body.ReplaceLineEndings(" ");
        var lines = text.EnumerateRunes().ToArray().Chunk(120).Select(chunk => string.Concat(chunk.Select(rune => rune.ToString()))).ToArray();
        const int pageSize = 5;
        var pages = Math.Max(1, (lines.Length + pageSize - 1) / pageSize);
        if (page > pages)
        {
            return ["Correio: página indisponível."];
        }

        return [$"Correio #{mailId} ({page}/{pages}): {message.Subject}",
            .. message.Items.Select(item => $"Correio item> {item.ItemKey} {item.Quantity}"),
            $"Correio ouro> {message.Gold}",
            $"Correio estado> {(message.ClaimedAt.HasValue ? "claimed" : "open")}",
            .. lines.Skip((page - 1) * pageSize).Take(pageSize).Select(line => "Correio> " + line)];
    }

    public async Task<MailMessage?> OpenAsync(long accountId, long mailId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var message = await connection.QuerySingleOrDefaultAsync<MailMessage>(new CommandDefinition(
            """
            SELECT m.id, m.subject, m.body, m.item_key AS ItemKey, m.item_count AS ItemCount, m.gold,
                   m.created_at AS CreatedAt, r.claimed_at AS ClaimedAt
            FROM game_mail m LEFT JOIN game_mail_receipts r ON r.mail_id = m.id AND r.account_id = @accountId
            WHERE m.id = @mailId AND (m.recipient_account_id IS NULL OR m.recipient_account_id = @accountId)
            """, new { accountId, mailId }, transaction, cancellationToken: ct));
        if (message is null)
        {
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_mail_receipts (mail_id, account_id, read_at)
            VALUES (@mailId, @accountId, @now)
            ON CONFLICT (mail_id, account_id) DO UPDATE
            SET read_at = COALESCE(game_mail_receipts.read_at, EXCLUDED.read_at)
            """, new { mailId, accountId, now = DateTime.UtcNow }, transaction, cancellationToken: ct));
        var items = await connection.QueryAsync<MailItem>(new CommandDefinition(
            "SELECT item_key AS ItemKey, quantity FROM game_mail_items WHERE mail_id = @mailId ORDER BY item_key",
            new { mailId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return message with { Items = items.ToArray() };
    }

    public async Task<IReadOnlyList<string>> ListAsync(long accountId)
    {
        await using var connection = await database.OpenAsync();
        var rows = (await connection.QueryAsync<MailSummary>(
            """
            SELECT id, subject FROM game_mail
            WHERE recipient_account_id IS NULL OR recipient_account_id = @accountId
            ORDER BY id DESC LIMIT 10
            """, new { accountId })).ToArray();
        return rows.Length == 0
            ? ["Correio: nenhuma mensagem."]
            : rows.Select(row => $"Correio #{row.Id}: {row.Subject}").ToArray();
    }

    public async Task<IReadOnlyList<string>> PendingItemsAsync(long accountId)
    {
        await using var connection = await database.OpenAsync();
        var rows = await connection.QueryAsync<MailSummary>(
            """
            SELECT m.id, m.subject
            FROM game_mail m
            LEFT JOIN game_mail_receipts r ON r.mail_id = m.id AND r.account_id = @accountId
            WHERE (m.recipient_account_id IS NULL OR m.recipient_account_id = @accountId)
              AND r.claimed_at IS NULL
              AND ((m.item_key IS NOT NULL AND m.item_count > 0)
                   OR EXISTS (SELECT 1 FROM game_mail_items i WHERE i.mail_id = m.id AND i.quantity > 0))
            ORDER BY m.id DESC LIMIT 10
            """, new { accountId });
        var summaries = rows.Select(row => $"Correio #{row.Id}: {row.Subject}").ToArray();
        return summaries.Length == 0 ? ["Correio: nenhum item pendente."] : summaries;
    }

    private sealed record MailSummary(long Id, string Subject);

    internal sealed record MailMessage(long Id, string Subject, string Body, string? ItemKey, int ItemCount, long Gold,
        DateTime CreatedAt, DateTime? ClaimedAt)
    {
        public MailItem[] Items { get; init; } = [];
    }

    internal sealed record MailItem(string ItemKey, int Quantity);
}
