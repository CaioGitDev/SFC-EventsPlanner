using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events.Fights;

public enum OutcomeChoice
{
    Red,
    Blue,
    Draw,
    NoContest,
}

public class ResultModel(EventService eventService) : PageModel
{
    /// <summary>Methods selectable when a corner wins (Draw/NoContest are outcomes, not choices here).</summary>
    public static readonly FightResultMethod[] WinnerMethods =
    [
        FightResultMethod.Ko,
        FightResultMethod.Tko,
        FightResultMethod.UnanimousDecision,
        FightResultMethod.SplitDecision,
        FightResultMethod.MajorityDecision,
        FightResultMethod.Disqualification,
        FightResultMethod.Forfeit,
    ];

    [BindProperty]
    public OutcomeChoice? Outcome { get; set; }

    [BindProperty]
    public FightResultMethod? Method { get; set; }

    [BindProperty]
    public int? Round { get; set; }

    [BindProperty]
    public string? Time { get; set; }

    public Event? Event { get; private set; }
    public Fight? Fight { get; private set; }

    /// <summary>True while showing the confirmation step (step 2 of 2).</summary>
    public bool Confirming { get; private set; }

    /// <summary>Summary shown on the confirmation step.</summary>
    public string? Summary { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();

        PopulateFromExistingResult();
        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();

        if (!Validate())
            return Page();

        Confirming = true;
        Summary = BuildSummary();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();

        if (!Validate())
            return Page();

        var result = await eventService.SaveResultAsync(eventId, fightId, ToInput(), ct);
        if (result != ResultOperationResult.Success)
        {
            ModelState.AddModelError(string.Empty, MessageFor(result));
            return Page();
        }

        TempData["Success"] = "Resultado gravado.";
        return RedirectToPage("/Admin/Events/Edit", new { id = eventId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();

        var result = await eventService.DeleteResultAsync(eventId, fightId, ct);
        if (result != ResultOperationResult.Success)
        {
            ModelState.AddModelError(string.Empty, MessageFor(result));
            return Page();
        }

        TempData["Success"] = "Resultado apagado. Os records dos atletas foram revertidos.";
        return RedirectToPage("/Admin/Events/Edit", new { id = eventId });
    }

    private async Task<bool> LoadAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(eventId, ct);
        Fight = Event?.Fights.SingleOrDefault(f => f.Id == fightId);
        return Event is not null && Fight is not null;
    }

    private void PopulateFromExistingResult()
    {
        var result = Fight!.Result;
        if (result is null)
            return;

        Outcome = result.Method switch
        {
            FightResultMethod.Draw => OutcomeChoice.Draw,
            FightResultMethod.NoContest => OutcomeChoice.NoContest,
            _ => result.WinnerAthleteId == Fight.RedCornerAthleteId
                ? OutcomeChoice.Red
                : OutcomeChoice.Blue,
        };
        if (Outcome is OutcomeChoice.Red or OutcomeChoice.Blue)
            Method = result.Method;
        Round = result.Round;
        Time = result.Time;
    }

    private bool Validate()
    {
        if (Outcome is null)
        {
            ModelState.AddModelError(nameof(Outcome), "Escolha o desfecho do combate.");
            return false;
        }

        if (Outcome is OutcomeChoice.Draw or OutcomeChoice.NoContest)
        {
            Method = null;
            Round = null;
            Time = null;
            return true;
        }

        if (Method is null || !WinnerMethods.Contains(Method.Value))
        {
            ModelState.AddModelError(nameof(Method), "Escolha o método de vitória.");
            return false;
        }

        // Round/time only make sense for stoppages; clear silently for the rest
        // so event-day entry stays a matter of taps, not error messages.
        if (Method is not (FightResultMethod.Ko or FightResultMethod.Tko
            or FightResultMethod.Disqualification))
        {
            Round = null;
            Time = null;
        }

        if (Method is FightResultMethod.Ko or FightResultMethod.Tko && Round is null)
        {
            ModelState.AddModelError(nameof(Round), "KO/TKO exigem o round.");
            return false;
        }

        if (Round is not null && (Round < 1 || Round > Fight!.Rounds))
        {
            ModelState.AddModelError(nameof(Round), $"O round tem de estar entre 1 e {Fight!.Rounds}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Time)
            && !System.Text.RegularExpressions.Regex.IsMatch(Time.Trim(), @"^\d{1,2}:[0-5]\d$"))
        {
            ModelState.AddModelError(nameof(Time), "Tempo no formato m:ss (ex.: 1:34).");
            return false;
        }

        return true;
    }

    private ResultInput ToInput() => Outcome switch
    {
        OutcomeChoice.Draw => new ResultInput(null, FightResultMethod.Draw, null, null),
        OutcomeChoice.NoContest => new ResultInput(null, FightResultMethod.NoContest, null, null),
        OutcomeChoice.Red => new ResultInput(Fight!.RedCornerAthleteId, Method!.Value, Round,
            NullIfBlank(Time)),
        _ => new ResultInput(Fight!.BlueCornerAthleteId, Method!.Value, Round, NullIfBlank(Time)),
    };

    private string BuildSummary() => Outcome switch
    {
        OutcomeChoice.Draw => "Empate",
        OutcomeChoice.NoContest => "No contest",
        _ => WinnerSummary(),
    };

    private string WinnerSummary()
    {
        var winner = Outcome == OutcomeChoice.Red ? Fight!.RedCornerAthlete : Fight!.BlueCornerAthlete;
        var name = winner is null ? "?" : $"{winner.FirstName} {winner.LastName}";
        var summary = $"Vitória de {name} por {Method!.Value.ToDisplay()}";
        if (Round is not null)
            summary += $" — round {Round}";
        if (!string.IsNullOrWhiteSpace(Time))
            summary += $", {Time!.Trim()}";
        return summary;
    }

    private static string MessageFor(ResultOperationResult result) => result switch
    {
        ResultOperationResult.EventCancelled => "Não é possível registar resultados num evento cancelado.",
        ResultOperationResult.EventNotYetHeld => "Só é possível registar resultados a partir da data do evento.",
        ResultOperationResult.FightNotScheduled => "Este combate não está agendado — reative-o primeiro.",
        ResultOperationResult.HasNoResult => "Este combate não tem resultado para apagar.",
        ResultOperationResult.InvalidInput => "Resultado inválido. Verifique o método, round e tempo.",
        _ => "Não foi possível gravar o resultado.",
    };

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
