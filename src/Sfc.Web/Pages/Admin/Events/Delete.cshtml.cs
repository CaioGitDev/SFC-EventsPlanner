using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class DeleteModel(EventService eventService) : PageModel
{
    public Event? Event { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(id, ct);
        return Event is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var result = await eventService.DeleteAsync(id, ct);
        switch (result)
        {
            case EventDeleteResult.NotFound:
                return NotFound();
            case EventDeleteResult.NotDeletable:
                Event = await eventService.GetWithCardAsync(id, ct);
                ModelState.AddModelError(string.Empty,
                    "Só é possível apagar eventos em rascunho ou cancelados. Cancele o evento primeiro.");
                return Page();
            default:
                TempData["Success"] = "Evento apagado.";
                return RedirectToPage("Index");
        }
    }
}
