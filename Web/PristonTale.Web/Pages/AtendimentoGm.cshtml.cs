using PristonTale.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Application.GameMasters;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AtendimentoGmModel(IAccountRepository accounts, SupportTickets tickets) : PageModel
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
        if (!await CanReplyAsync(ct))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(resposta) || resposta.Length > 4000)
        {
            return BadRequest();
        }

        return await tickets.ReplyAsync(accountId, ticketId, User.Identity!.Name!, resposta.Trim(), ct)
            ? RedirectToPage()
            : new StatusCodeResult(StatusCodes.Status409Conflict);
    }

    public async Task<IActionResult> OnPostFecharAsync(long accountId, Guid ticketId, CancellationToken ct)
    {
        if (!await CanReplyAsync(ct))
        {
            return Forbid();
        }

        return await tickets.CloseAsync(accountId, ticketId, User.Identity!.Name!, ct)
            ? RedirectToPage()
            : new StatusCodeResult(StatusCodes.Status409Conflict);
    }

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

    private async Task<bool> CanReplyAsync(CancellationToken ct) =>
        User.Identity?.Name is { } name &&
        await accounts.FindByUserIdAsync(name, ct) is { IsBlocked: false, GmLevel: GameMasterLevel.Administrator };
}
