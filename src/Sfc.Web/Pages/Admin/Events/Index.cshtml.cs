using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class IndexModel(EventService eventService) : PageModel
{
    public List<EventListItem> Events { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public EventStatus? Status { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
        => Events = await eventService.SearchAsync(Search, Status, ct);
}
