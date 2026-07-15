using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class ClubForm
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(200, ErrorMessage = "Máximo de 200 caracteres.")]
    public string Name { get; set; } = "";

    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; }

    [EmailAddress(ErrorMessage = "Email inválido.")]
    [StringLength(200)]
    public string? ContactEmail { get; set; }

    [StringLength(50)] public string? ContactPhone { get; set; }

    public string? CoachesText { get; set; }

    public ClubInput ToInput()
        => new(Name, City, Country, ContactEmail, ContactPhone, CoachesText);
}

public class CreateModel(ClubService clubService) : PageModel
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    [BindProperty]
    public ClubForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Logo { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Logo is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Logo", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await using var logoStream = Logo?.OpenReadStream();
            await clubService.CreateAsync(Form.ToInput(), logoStream, ct);
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Logo", "O ficheiro não é uma imagem válida.");
            return Page();
        }

        TempData["Success"] = "Clube criado com sucesso.";
        return RedirectToPage("Index");
    }
}
