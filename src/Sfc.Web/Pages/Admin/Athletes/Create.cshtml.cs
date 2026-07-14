using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class AthleteForm
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(100)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "O apelido é obrigatório.")]
    [StringLength(100)]
    public string LastName { get; set; } = "";

    [StringLength(100)] public string? Nickname { get; set; }

    [Required(ErrorMessage = "A data de nascimento é obrigatória.")]
    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "A nacionalidade é obrigatória.")]
    [StringLength(100)]
    public string Nationality { get; set; } = "Portugal";

    [Required(ErrorMessage = "A disciplina é obrigatória.")]
    public Discipline? Discipline { get; set; }

    [Required(ErrorMessage = "O estatuto é obrigatório.")]
    public AthleteStatus? Status { get; set; }

    public Guid? ClubId { get; set; }
    [StringLength(200)] public string? CoachName { get; set; }
    [StringLength(50)] public string? WeightClass { get; set; }

    [Range(20, 200, ErrorMessage = "Peso entre 20 e 200 kg.")]
    public decimal? WeightKg { get; set; }

    [Range(100, 230, ErrorMessage = "Altura entre 100 e 230 cm.")]
    public int? HeightCm { get; set; }

    public bool PublicProfileConsent { get; set; }

    [StringLength(200)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug inválido: usar apenas minúsculas, números e hífens.")]
    public string? Slug { get; set; }

    public AthleteInput ToInput()
        => new(FirstName, LastName, Nickname, DateOfBirth!.Value, Nationality,
            Discipline!.Value, Status!.Value, ClubId, CoachName, WeightClass,
            WeightKg, HeightCm, PublicProfileConsent, Slug);
}

public class CreateModel(AthleteService athleteService, ClubService clubService) : PageModel
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    [BindProperty]
    public AthleteForm Form { get; set; } = new();

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineWins { get; set; }

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineLosses { get; set; }

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineDraws { get; set; }

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineKos { get; set; }

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public List<SelectListItem> ClubOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => await LoadClubsAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Photo is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Photo", "A imagem não pode exceder 10 MB.");
        if (BaselineKos > BaselineWins)
            ModelState.AddModelError("BaselineKos", "Os KOs não podem exceder as vitórias.");

        if (!ModelState.IsValid)
        {
            await LoadClubsAsync(ct);
            return Page();
        }

        try
        {
            await using var photoStream = Photo?.OpenReadStream();
            await athleteService.CreateAsync(Form.ToInput(),
                (BaselineWins, BaselineLosses, BaselineDraws, BaselineKos), photoStream, ct);
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Photo", "O ficheiro não é uma imagem válida.");
            await LoadClubsAsync(ct);
            return Page();
        }

        TempData["Success"] = "Atleta criado com sucesso.";
        return RedirectToPage("Index");
    }

    private async Task LoadClubsAsync(CancellationToken ct)
        => ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
}
