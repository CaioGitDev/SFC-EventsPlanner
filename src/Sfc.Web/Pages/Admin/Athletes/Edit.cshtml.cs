using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class EditModel(AthleteService athleteService, ClubService clubService) : PageModel
{
    [BindProperty]
    public AthleteForm Form { get; set; } = new();

    [BindProperty]
    public bool IsActive { get; set; } = true;

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public string? CurrentPhotoUrl { get; private set; }
    public string RecordDisplay { get; private set; } = "";
    public List<SelectListItem> ClubOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var athlete = await athleteService.GetAsync(id, ct);
        if (athlete is null)
            return NotFound();

        Form = new AthleteForm
        {
            FirstName = athlete.FirstName,
            LastName = athlete.LastName,
            Nickname = athlete.Nickname,
            DateOfBirth = athlete.DateOfBirth,
            Nationality = athlete.Nationality,
            Discipline = athlete.Discipline,
            Status = athlete.Status,
            ClubId = athlete.ClubId,
            CoachName = athlete.CoachName,
            WeightClass = athlete.WeightClass,
            WeightKg = athlete.WeightKg,
            HeightCm = athlete.HeightCm,
            PublicProfileConsent = athlete.PublicProfileConsent,
            Slug = athlete.Slug,
            Notes = athlete.Notes,
        };
        IsActive = athlete.IsActive;
        CurrentPhotoUrl = athlete.PhotoUrl;
        RecordDisplay = athlete.RecordDisplay;
        await LoadClubsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (Photo is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Photo", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
        {
            await RestoreDisplayStateAsync(id, ct);
            return Page();
        }

        try
        {
            await using var photoStream = Photo?.OpenReadStream();
            var athlete = await athleteService.UpdateAsync(id, Form.ToInput(), IsActive, photoStream, ct);
            if (athlete is null)
                return NotFound();
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Photo", "O ficheiro não é uma imagem válida.");
            await RestoreDisplayStateAsync(id, ct);
            return Page();
        }

        TempData["Success"] = "Atleta atualizado.";
        return RedirectToPage("Index");
    }

    private async Task RestoreDisplayStateAsync(Guid id, CancellationToken ct)
    {
        var athlete = await athleteService.GetAsync(id, ct);
        CurrentPhotoUrl = athlete?.PhotoUrl;
        RecordDisplay = athlete?.RecordDisplay ?? "";
        await LoadClubsAsync(ct);
    }

    private async Task LoadClubsAsync(CancellationToken ct)
        => ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
}
