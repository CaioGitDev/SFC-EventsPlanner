using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Clubs;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class DeleteModel(ClubService clubService) : PageModel
{
    public Club? Club { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Club = await clubService.GetAsync(id, ct);
        return Club is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var result = await clubService.DeleteAsync(id, ct);
        switch (result)
        {
            case ClubDeleteResult.NotFound:
                return NotFound();
            case ClubDeleteResult.HasAthletes:
                Club = await clubService.GetAsync(id, ct);
                ModelState.AddModelError(string.Empty,
                    "Não é possível apagar um clube com atletas associados. Reatribua ou remova os atletas primeiro.");
                return Page();
            default:
                TempData["Success"] = "Clube apagado.";
                return RedirectToPage("Index");
        }
    }
}
