using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class DeleteModel(AthleteService athleteService) : PageModel
{
    public Athlete? Athlete { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Athlete = await athleteService.GetAsync(id, ct);
        return Athlete is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (!await athleteService.DeleteAsync(id, ct))
            return NotFound();

        TempData["Success"] = "Atleta apagado.";
        return RedirectToPage("Index");
    }
}
