using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events.Fights;

public class FightForm
{
    [Required(ErrorMessage = "O canto vermelho é obrigatório.")]
    public Guid? RedCornerAthleteId { get; set; }

    [Required(ErrorMessage = "O canto azul é obrigatório.")]
    public Guid? BlueCornerAthleteId { get; set; }

    [Required(ErrorMessage = "A disciplina é obrigatória.")]
    public Discipline? Discipline { get; set; }

    [Range(1, 12, ErrorMessage = "Rounds entre 1 e 12.")]
    public int Rounds { get; set; } = 3;

    [Range(1, 10, ErrorMessage = "Duração entre 1 e 10 minutos.")]
    public int RoundDurationMinutes { get; set; } = 3;

    [StringLength(50)]
    public string? WeightClass { get; set; }

    [Range(20, 200, ErrorMessage = "Peso combinado entre 20 e 200 kg.")]
    public decimal? CatchweightKg { get; set; }

    public bool IsTitleFight { get; set; }
    public bool IsAmateur { get; set; }

    public FightInput ToInput()
        => new(RedCornerAthleteId!.Value, BlueCornerAthleteId!.Value, Discipline!.Value,
            Rounds, RoundDurationMinutes, WeightClass, CatchweightKg, IsTitleFight, IsAmateur);
}

public class AddModel(EventService eventService, AthleteService athleteService,
    ClubService clubService) : PageModel
{
    [BindProperty]
    public FightForm Form { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FilterName { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? FilterClubId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Discipline? FilterDiscipline { get; set; }

    public Event? Event { get; private set; }
    public List<SelectListItem> AthleteOptions { get; private set; } = [];
    public List<SelectListItem> ClubOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid eventId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, ct))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid eventId, CancellationToken ct)
    {
        ValidateWeightXor();
        if (Form.RedCornerAthleteId is not null && Form.RedCornerAthleteId == Form.BlueCornerAthleteId)
            ModelState.AddModelError("Form.BlueCornerAthleteId",
                "O mesmo atleta não pode estar nos dois cantos.");

        if (!ModelState.IsValid)
            return await ReloadAsync(eventId, ct);

        var result = await eventService.AddFightAsync(eventId, Form.ToInput(), ct);
        switch (result)
        {
            case CardOperationResult.EventNotFound:
                return NotFound();
            case CardOperationResult.EventLocked:
                ModelState.AddModelError(string.Empty,
                    "O card de um evento concluído ou cancelado não pode ser alterado.");
                return await ReloadAsync(eventId, ct);
            case CardOperationResult.AthleteAlreadyInCard:
                ModelState.AddModelError(string.Empty,
                    "Um dos atletas já tem combate neste evento.");
                return await ReloadAsync(eventId, ct);
            case CardOperationResult.SameAthleteBothCorners:
                ModelState.AddModelError(string.Empty,
                    "O mesmo atleta não pode estar nos dois cantos.");
                return await ReloadAsync(eventId, ct);
            default:
                TempData["Success"] = "Combate adicionado ao card.";
                return RedirectToPage("/Admin/Events/Edit", new { id = eventId });
        }
    }

    private void ValidateWeightXor()
    {
        var hasWeightClass = !string.IsNullOrWhiteSpace(Form.WeightClass);
        if (hasWeightClass == Form.CatchweightKg.HasValue)
            ModelState.AddModelError("Form.WeightClass",
                "Indique a categoria de peso OU peso combinado (exatamente um).");
    }

    private async Task<IActionResult> ReloadAsync(Guid eventId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, ct))
            return NotFound();
        return Page();
    }

    private async Task<bool> LoadAsync(Guid eventId, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(eventId, ct);
        if (Event is null)
            return false;

        AthleteOptions = (await athleteService.ListActiveOptionsAsync(FilterName, FilterClubId, FilterDiscipline, ct))
            .Select(o => new SelectListItem(o.Label, o.Id.ToString()))
            .ToList();
        ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
        return true;
    }
}
