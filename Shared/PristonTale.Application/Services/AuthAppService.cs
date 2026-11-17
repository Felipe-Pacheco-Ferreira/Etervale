using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Contracts.Sessions;

namespace PristonTale.Application.Services;

public sealed class AuthAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly IAccountRepository _accounts;
    private readonly AccountAppService _credentials;
    private readonly IBrowserLoginTickets? _browserTickets;

    public AuthAppService(ISessionRegistry sessions, IAccountRepository accounts, IBrowserLoginTickets? browserTickets = null)
    {
        _sessions = sessions;
        _accounts = accounts;
        _credentials = new AccountAppService(accounts);
        _browserTickets = browserTickets;
    }

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        var check = await _credentials.CheckAsync(account, password, ct);
        if (check.Account is not { } record)
        {
            return LoginOutcome.Denied(check.Failure);
        }

        return await CreateSessionAsync(record, ct);
    }

    public async Task<LoginOutcome> LoginWithBrowserTicketAsync(string account, string ticket, CancellationToken ct = default)
    {
        if (_browserTickets is null || !BrowserLoginTicket.IsTicket(ticket)
            || await _accounts.FindByUserIdAsync(account, ct) is not { IsBlocked: false } record
            || !await _browserTickets.RedeemAsync(BrowserLoginTicket.Digest(ticket), record.Id,
                BrowserLoginTicket.Digest(record.PasswordHash), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ct))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        return await CreateSessionAsync(record, ct);
    }

    private async Task<LoginOutcome> CreateSessionAsync(AccountRecord record, CancellationToken ct)
    {
        await _accounts.UpdateLastLoginAsync(record.Id, ct);
        var session = _sessions.Create(record.Id, record.UserId);
        session.GmLevel = Math.Clamp(record.GmLevel, 0, GameMasters.GameMasterLevel.Highest);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }
}
