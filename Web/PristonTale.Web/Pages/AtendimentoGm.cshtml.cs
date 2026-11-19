using PristonTale.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Application.GameMasters;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AtendimentoGmModel(IAccountRepository accounts, SupportTickets tickets, IGameMasterRepository gameMasters) : PageModel
{
    public IReadOnlyList<(long AccountId, SupportTicket Ticket)> Queue { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await CanReplyAsync(ct))
        {
            return Forbid();
        }

        Queue = await tickets.QueueAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long accountId, Guid ticketId, string? resposta, CancellationToken ct)
    {
        if (await StaffAsync(ct) is not { } staff)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(resposta) || resposta.Length > 4000)
        {
            return BadRequest();
        }

        if (!await tickets.ReplyAsync(accountId, ticketId, staff.UserId, resposta.Trim(), ct))
        {
            return new StatusCodeResult(StatusCodes.Status409Conflict);
        }

        await AuditAsync(staff, "web: ticket reply " + ticketId.ToString("N"), resposta.Trim(), ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFecharAsync(long accountId, Guid ticketId, CancellationToken ct)
    {
        if (await StaffAsync(ct) is not { } staff)
        {
            return Forbid();
        }

        if (!await tickets.CloseAsync(accountId, ticketId, staff.UserId, ct))
        {
            return new StatusCodeResult(StatusCodes.Status409Conflict);
        }

        await AuditAsync(staff, "web: ticket close " + ticketId.ToString("N"), "closed", ct);
        return RedirectToPage();
    }

    // Answers and closings from the site are in the game masters' audit trail like the game's and Discord's.
    private Task AuditAsync(AccountRecord staff, string command, string result, CancellationToken ct) =>
        gameMasters.AppendAuditAsync(new GmAuditEntry(staff.Id, staff.UserId, "", staff.GmLevel, command, GameMasterLevel.Administrator, true,
            result.Length > AuditResultLimit ? result[..AuditResultLimit] : result), ct);

    private const int AuditResultLimit = 1000;

    public bool HasAttachment(long accountId, Guid ticketId) => tickets.Attachment(accountId, ticketId) is not null;

    public async Task<IActionResult> OnGetAnexoAsync(long accountId, Guid ticketId, CancellationToken ct)
    {
        if (!await CanReplyAsync(ct))
        {
            return Forbid();
        }

        return tickets.Attachment(accountId, ticketId) is { } attachment
            ? PhysicalFile(Path.GetFullPath(attachment), "application/octet-stream", "anexo" + Path.GetExtension(attachment))
            : NotFound();
    }

    private async Task<bool> CanReplyAsync(CancellationToken ct) => await StaffAsync(ct) is not null;

    // The signed-in administrator, or null.
    private async Task<AccountRecord?> StaffAsync(CancellationToken ct) =>
        User.Identity?.Name is { } name && await accounts.FindByUserIdAsync(name, ct) is { IsBlocked: false, GmLevel: GameMasterLevel.Administrator } staff
            ? staff
            : null;
}
