using PristonTale.Application.Support;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SuporteModel(IAccountRepository accounts, SupportTickets tickets) : PageModel
{
    [BindProperty, Required, StringLength(100)]
    public string Assunto { get; set; } = "";

    [BindProperty, Required, StringLength(4000)]
    public string Mensagem { get; set; } = "";

    public IReadOnlyList<SupportTicket> Tickets { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (await CurrentAccount(ct) is not { } account)
        {
            return Forbid();
        }

        Tickets = await tickets.ListAsync(account.Id, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (await CurrentAccount(ct) is not { } account)
        {
            return Forbid();
        }

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Assunto) || string.IsNullOrWhiteSpace(Mensagem))
        {
            ModelState.AddModelError("", "Preencha o assunto e a mensagem.");
            Tickets = await tickets.ListAsync(account.Id, ct);
            return Page();
        }

        await tickets.CreateAsync(account.Id, Assunto.Trim(), Mensagem.Trim(), account.UserId, ct);
        return RedirectToPage();
    }

    private async Task<AccountRecord?> CurrentAccount(CancellationToken ct) =>
        User.Identity?.Name is { } name ? await accounts.FindByUserIdAsync(name, ct) : null;
}
