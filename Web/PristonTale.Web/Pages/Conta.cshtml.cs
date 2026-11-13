using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Web.Hosting;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Simulation.World;

namespace PristonTale.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContaModel(IAccountRepository accounts, ICharacterRepository characters, IClanRepository clans, WorldCatalog fields) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "nova")]
    public bool Nova { get; set; }

    public AccountRecord Account { get; private set; } = null!;
    public IReadOnlyList<CharacterRecord> Characters { get; private set; } = [];
    public IReadOnlyDictionary<long, string> Clans { get; private set; } = new Dictionary<long, string>();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (User.Identity?.Name is not { } name || await accounts.FindByUserIdAsync(name, ct) is not { } account)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect(SitePaths.SignIn);
        }

        Account = account;
        Characters = await characters.ListByAccountAsync(account.Id, ct);
        Clans = await clans.ClanNamesAsync(account.Id, ct);
        return Page();
    }

    public string FieldName(int fieldId) => fields.FieldName(fieldId);
}
