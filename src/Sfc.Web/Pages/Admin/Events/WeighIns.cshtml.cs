using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class WeighInsModel(EventService eventService) : PageModel
{
    // Bound as strings: HTML number inputs post an invariant "71.8" while pt-PT model
    // binding expects "71,8" — parse manually accepting both separators.
    [BindProperty]
    public string? OfficialWeightKg { get; set; }

    [BindProperty]
    public string? ExpectedWeightKg { get; set; }

    [BindProperty]
    public bool IsApproved { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    public Event? Event { get; private set; }
    public List<WeighInRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid eventId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, ct))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid eventId, Guid fightId, Guid athleteId,
        CancellationToken ct)
    {
        if (!TryParseWeight(OfficialWeightKg, out var official)
            || !TryParseWeight(ExpectedWeightKg, out var expected))
        {
            TempData["Error"] = "Peso inválido — use números, ex.: 71,8.";
            return RedirectToPage(new { eventId });
        }

        var result = await eventService.SaveWeighInAsync(eventId, fightId, athleteId,
            new WeighInInput(official, expected, IsApproved, Notes), ct);

        switch (result)
        {
            case WeighInOperationResult.EventNotFound:
            case WeighInOperationResult.FightNotFound:
                return NotFound();
            case WeighInOperationResult.Success:
                TempData["Success"] = "Pesagem gravada.";
                break;
            default:
                TempData["Error"] = MessageFor(result);
                break;
        }

        return RedirectToPage(new { eventId });
    }

    private async Task<bool> LoadAsync(Guid eventId, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(eventId, ct);
        if (Event is null)
            return false;

        Rows = await eventService.GetWeighInSummaryAsync(eventId, ct);
        return true;
    }

    private static bool TryParseWeight(string? value, out decimal? weight)
    {
        weight = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!decimal.TryParse(value.Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return false;

        weight = parsed;
        return true;
    }

    private static string MessageFor(WeighInOperationResult result) => result switch
    {
        WeighInOperationResult.AthleteNotInFight => "O atleta já não pertence a este combate — atualize a página.",
        WeighInOperationResult.EventCancelled => "Não é possível registar pesagens num evento cancelado.",
        WeighInOperationResult.ApprovalRequiresWeight => "Introduza o peso oficial antes de aprovar a pesagem.",
        WeighInOperationResult.InvalidInput => "Pesagem inválida. Verifique os pesos introduzidos.",
        _ => "Não foi possível gravar a pesagem.",
    };
}
