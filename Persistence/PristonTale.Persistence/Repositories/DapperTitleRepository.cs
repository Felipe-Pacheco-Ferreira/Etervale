using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Titles;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperTitleRepository(IDbConnectionFactory connectionFactory) : ITitleRepository
{
    public async Task<IReadOnlyList<TitleHolderRecord>> HoldersAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<TitleHolderRecord>(new CommandDefinition(
            "SELECT title, character_id, name FROM title_holders ORDER BY title", cancellationToken: ct));
        return rows.AsList();
    }

    public async Task SetHolderAsync(TitleHolderRecord holder, DateTime nowUtc, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO title_holders (title, character_id, name, since) VALUES (@Title, @CharacterId, @Name, @Since)
            ON CONFLICT (title) DO UPDATE SET character_id = excluded.character_id, name = excluded.name, since = excluded.since
            """,
            new { holder.Title, holder.CharacterId, holder.Name, Since = nowUtc }, cancellationToken: ct));
    }
}
