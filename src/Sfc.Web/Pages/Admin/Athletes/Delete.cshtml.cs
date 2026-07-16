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
        var result = await athleteService.DeleteAsync(id, ct);
        switch (result)
        {
            case AthleteDeleteResult.NotFound:
                return NotFound();
            case AthleteDeleteResult.HasFights:
                Athlete = await athleteService.GetAsync(id, ct);
                ModelState.AddModelError(string.Empty,
                    "Não é possível apagar um atleta com combates registados. Use antes o estado \"Inativo\".");
                return Page();
            default:
                TempData["Success"] = "Atleta apagado.";
                return RedirectToPage("Index");
        }
    }
}
