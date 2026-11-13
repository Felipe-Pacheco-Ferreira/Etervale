using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Pages;

// Signing out is a form post (with its anti-forgery token), so no link or image elsewhere can end a session.
public sealed class SairModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect(SitePaths.Account);

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect(SitePaths.Home);
    }
}
