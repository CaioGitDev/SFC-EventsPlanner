using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class EventForm
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(4000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "A data é obrigatória.")]
    public DateTime? Date { get; set; }

    [StringLength(200)] public string? Venue { get; set; }
    [StringLength(100)] public string? City { get; set; }

    [Url(ErrorMessage = "Link inválido.")]
    [StringLength(500)]
    public string? TicketsUrl { get; set; }

    [Url(ErrorMessage = "Link inválido.")]
    [StringLength(500)]
    public string? StreamUrl { get; set; }

    [StringLength(200)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug inválido: usar apenas minúsculas, números e hífens.")]
    public string? Slug { get; set; }

    public EventInput ToInput()
        => new(Name, Description, Date!.Value, Venue, City, TicketsUrl, StreamUrl, Slug);
}

public class CreateModel(EventService eventService) : PageModel
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    [BindProperty]
    public EventForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Banner { get; set; }

    [BindProperty]
    public IFormFile? Poster { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Banner is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Banner", "A imagem não pode exceder 10 MB.");
        if (Poster is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Poster", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await using var bannerStream = Banner?.OpenReadStream();
            await using var posterStream = Poster?.OpenReadStream();
            var evt = await eventService.CreateAsync(Form.ToInput(), bannerStream, posterStream, ct);
            TempData["Success"] = "Evento criado com sucesso.";
            return RedirectToPage("Edit", new { id = evt.Id });
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Banner", "O ficheiro não é uma imagem válida.");
            return Page();
        }
    }
}
