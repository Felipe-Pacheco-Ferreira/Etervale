using Microsoft.AspNetCore.Mvc.RazorPages;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

public sealed class EventosModel(SiteData data) : PageModel
{
    public SiteCalendar Calendar { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Calendar = await data.CalendarAsync(ct);

    // The lines under an event's card: the experience event's share, the drops of the drop events.
    public IEnumerable<string> Details(CalendarEventKind kind)
    {
        var calendar = Calendar.Calendar;
        if (kind == CalendarEventKind.ExperienceEvent)
        {
            yield return $"Cada monstro abatido dá {calendar.ExperiencePercent}% da experiência.";
        }
        else if (kind == CalendarEventKind.DropEvents)
        {
            foreach (var (drop, name) in SiteText.DropNames)
            {
                if ((calendar.Drops & drop) != 0)
                {
                    yield return name;
                }
            }
        }
    }
}
