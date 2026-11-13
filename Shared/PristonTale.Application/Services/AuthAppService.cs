using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Contracts.Sessions;

namespace PristonTale.Application.Services;

public sealed class AuthAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly IAccountRepository _accounts;
    private readonly AccountAppService _credentials;

    public AuthAppService(ISessionRegistry sessions, IAccountRepository accounts)
    {
        _sessions = sessions;
        _accounts = accounts;
        _credentials = new AccountAppService(accounts);
    }

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        var check = await _credentials.CheckAsync(account, password, ct);
        if (check.Account is not { } record)
        {
            return LoginOutcome.Denied(check.Failure);
        }

        await _accounts.UpdateLastLoginAsync(record.Id, ct);
        var session = _sessions.Create(record.Id, record.UserId);
        session.GmLevel = Math.Clamp(record.GmLevel, 0, GameMasters.GameMasterLevel.Highest);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }
}
