using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

public sealed class RankingsModel(SiteData data) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "classe")]
    public string? ClassSlug { get; set; }

    public PlayerClass? Class { get; private set; }
    public IReadOnlyList<RankedCharacter> Top { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Class = ClassTitles.FromSlug(ClassSlug);
        Top = await data.TopAsync(Class, ct);
    }
}
