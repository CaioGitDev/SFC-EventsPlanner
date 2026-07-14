using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class EditModel(ClubService clubService) : PageModel
{
    [BindProperty]
    public ClubForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Logo { get; set; }

    public string? CurrentLogoUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var club = await clubService.GetAsync(id, ct);
        if (club is null)
            return NotFound();

        Form = new ClubForm
        {
            Name = club.Name,
            City = club.City,
            Country = club.Country,
            ContactEmail = club.ContactEmail,
            ContactPhone = club.ContactPhone,
            CoachesText = string.Join("\n",
                club.Coaches.Select(c => c.Contact is null ? c.Name : $"{c.Name}; {c.Contact}")),
        };
        CurrentLogoUrl = club.LogoUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (Logo is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Logo", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await using var logoStream = Logo?.OpenReadStream();
            var club = await clubService.UpdateAsync(id, Form.ToInput(), logoStream, ct);
            if (club is null)
                return NotFound();
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Logo", "O ficheiro não é uma imagem válida.");
            return Page();
        }

        TempData["Success"] = "Clube atualizado.";
        return RedirectToPage("Index");
    }
}
