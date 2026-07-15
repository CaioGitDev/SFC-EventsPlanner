using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Clubs;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class IndexModel(ClubService clubService) : PageModel
{
    public List<Club> Clubs { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
        => Clubs = await clubService.SearchAsync(Search, ct);
}
