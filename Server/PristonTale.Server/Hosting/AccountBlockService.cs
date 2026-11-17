using Dapper;
using PristonTale.Application.Chat;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Server.Hosting;

internal sealed class AccountBlockService(ChatDirectory chat, IDbConnectionFactory database,
    ILogger<AccountBlockService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var members = chat.Members.ToArray();
            if (members.Length == 0)
            {
                continue;
            }

            try
            {
                await using var connection = await database.OpenAsync(stoppingToken);
                var blocked = (await connection.QueryAsync<long>(new CommandDefinition(
                    "SELECT id FROM accounts WHERE is_blocked", cancellationToken: stoppingToken))).ToHashSet();
                foreach (var member in members)
                {
                    if (blocked.Contains(member.AccountId))
                    {
                        member.Link.Disconnect("account blocked");
                    }
                }
            }
            catch (System.Data.Common.DbException exception)
            {
                logger.LogWarning(exception, "Could not check connected accounts for blocks");
            }
        }
    }
}
