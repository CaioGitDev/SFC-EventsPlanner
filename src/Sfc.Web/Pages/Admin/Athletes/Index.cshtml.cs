using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class IndexModel(AthleteService athleteService, ClubService clubService) : PageModel
{
    public AthleteSearchResult Result { get; private set; } =
        new([], 0, 1, AthleteService.PageSize);

    public List<SelectListItem> ClubOptions { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ClubId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Discipline? Discipline { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Result = await athleteService.SearchAsync(Search, ClubId, Discipline, P, ct);
        ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
    }
}
