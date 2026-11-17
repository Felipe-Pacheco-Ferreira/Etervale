using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Application.Support;
using PristonTale.Persistence.Abstractions.Accounts;

namespace PristonTale.Web.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AnexoTicketModel(IAccountRepository accounts, SupportTickets tickets) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid TicketId { get; set; }

    [BindProperty]
    public IFormFile? Arquivo { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) =>
        await FindOwnerAsync(ct) is { } account && tickets.Exists(account.Id, TicketId) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (await FindOwnerAsync(ct) is not { } account || !tickets.Exists(account.Id, TicketId))
        {
            return NotFound();
        }

        if (Arquivo is { Length: > 0 and <= SupportTickets.MaximumAttachmentBytes } file)
        {
            await using var content = file.OpenReadStream();
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".jpeg")
            {
                extension = ".jpg";
            }

            if (await tickets.AttachAsync(account.Id, TicketId, content, extension, ct))
            {
                return RedirectToPage("/Suporte");
            }
        }

        ModelState.AddModelError("", "Escolha uma imagem ou vídeo permitido de até 20 MB. Cada ticket aceita um anexo.");
        return Page();
    }

    private async Task<AccountRecord?> FindOwnerAsync(CancellationToken ct) =>
        User.Identity?.Name is { } name && await accounts.FindByUserIdAsync(name, ct) is { IsBlocked: false } account ? account : null;
}
