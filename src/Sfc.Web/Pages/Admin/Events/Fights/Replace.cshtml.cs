using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events.Fights;

public class ReplaceModel(EventService eventService, AthleteService athleteService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Escolha o canto a substituir.")]
    public Corner? Corner { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Escolha o novo atleta.")]
    public Guid? NewAthleteId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterName { get; set; }

    public Event? Event { get; private set; }
    public Fight? Fight { get; private set; }
    public List<SelectListItem> AthleteOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return await ReloadAsync(eventId, fightId, ct);

        var result = await eventService.ReplaceAthleteAsync(eventId, fightId, Corner!.Value,
            NewAthleteId!.Value, ct);
        switch (result)
        {
            case CardOperationResult.EventNotFound:
            case CardOperationResult.FightNotFound:
                return NotFound();
            case CardOperationResult.FightNotScheduled:
                ModelState.AddModelError(string.Empty,
                    "Só é possível substituir atletas em combates agendados.");
                return await ReloadAsync(eventId, fightId, ct);
            case CardOperationResult.AthleteAlreadyInCard:
                ModelState.AddModelError(string.Empty,
                    "O atleta escolhido já tem combate neste evento.");
                return await ReloadAsync(eventId, fightId, ct);
            default:
                TempData["Success"] = "Atleta substituído.";
                return RedirectToPage("/Admin/Events/Edit", new { id = eventId });
        }
    }

    private async Task<IActionResult> ReloadAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();
        return Page();
    }

    private async Task<bool> LoadAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(eventId, ct);
        Fight = Event?.Fights.SingleOrDefault(f => f.Id == fightId);
        if (Event is null || Fight is null)
            return false;

        AthleteOptions = (await athleteService.ListActiveOptionsAsync(FilterName, null, null, ct))
            .Select(o => new SelectListItem(o.Label, o.Id.ToString()))
            .ToList();
        return true;
    }
}
