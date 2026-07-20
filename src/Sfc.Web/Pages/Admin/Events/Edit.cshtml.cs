using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class EditModel(EventService eventService) : PageModel
{
    [BindProperty]
    public EventForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Banner { get; set; }

    [BindProperty]
    public IFormFile? Poster { get; set; }

    public Event? Event { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(id, ct);
        if (Event is null)
            return NotFound();

        PopulateFormFrom(Event);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (Banner is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Banner", "A imagem não pode exceder 10 MB.");
        if (Poster is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Poster", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return await ReloadAsync(id, ct);

        try
        {
            await using var bannerStream = Banner?.OpenReadStream();
            await using var posterStream = Poster?.OpenReadStream();
            var evt = await eventService.UpdateAsync(id, Form.ToInput(), bannerStream, posterStream, ct);
            if (evt is null)
                return NotFound();
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Banner", "O ficheiro não é uma imagem válida.");
            return await ReloadAsync(id, ct);
        }

        TempData["Success"] = "Evento atualizado.";
        return RedirectToPage(new { id });
    }

    public Task<IActionResult> OnPostPublishAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.PublishAsync(id, ct), "Evento publicado.", ct);

    public Task<IActionResult> OnPostUnpublishAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.UnpublishAsync(id, ct), "Evento despublicado.", ct);

    public Task<IActionResult> OnPostCompleteAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.CompleteAsync(id, ct), "Evento concluído.", ct);

    public Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.CancelAsync(id, ct), "Evento cancelado.", ct);

    public async Task<IActionResult> OnPostMoveUpAsync(Guid id, Guid fightId, CancellationToken ct)
    {
        await eventService.MoveFightAsync(id, fightId, MoveDirection.Up, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveDownAsync(Guid id, Guid fightId, CancellationToken ct)
    {
        await eventService.MoveFightAsync(id, fightId, MoveDirection.Down, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveFightAsync(Guid id, Guid fightId, CancellationToken ct)
    {
        var result = await eventService.RemoveFightAsync(id, fightId, ct);
        if (result == CardOperationResult.HasResult)
            TempData["Error"] = "Este combate tem resultado — apague primeiro o resultado para reverter os records.";
        else
            TempData["Success"] = result == CardOperationResult.Success ? "Combate removido." : null;
        return RedirectToPage(new { id });
    }

    public Task<IActionResult> OnPostCancelFightAsync(Guid id, Guid fightId, CancellationToken ct)
        => FightStatusActionAsync(id, () => eventService.CancelFightAsync(id, fightId, ct), "Combate cancelado.");

    public Task<IActionResult> OnPostNoContestFightAsync(Guid id, Guid fightId, CancellationToken ct)
        => FightStatusActionAsync(id, () => eventService.MarkFightNoContestAsync(id, fightId, ct), "Combate marcado como no contest.");

    public Task<IActionResult> OnPostReinstateFightAsync(Guid id, Guid fightId, CancellationToken ct)
        => FightStatusActionAsync(id, () => eventService.ReinstateFightAsync(id, fightId, ct), "Combate reativado.");

    private async Task<IActionResult> FightStatusActionAsync(Guid id,
        Func<Task<ResultOperationResult>> action, string successMessage)
    {
        var result = await action();
        switch (result)
        {
            case ResultOperationResult.EventNotFound:
            case ResultOperationResult.FightNotFound:
                return NotFound();
            case ResultOperationResult.Success:
                TempData["Success"] = successMessage;
                break;
            default:
                TempData["Error"] = "Não foi possível alterar o estado do combate.";
                break;
        }

        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> TransitionAsync(Guid id, Func<Task<EventTransitionResult>> action,
        string successMessage, CancellationToken ct)
    {
        var result = await action();
        switch (result)
        {
            case EventTransitionResult.NotFound:
                return NotFound();
            case EventTransitionResult.HasResults:
                TempData["Error"] =
                    "Não é possível cancelar um evento com resultados gravados. Apague primeiro os resultados para reverter os records.";
                return RedirectToPage(new { id });
            case EventTransitionResult.InvalidTransition:
                // Transition POSTs carry no Form.* fields, so binding left Form blank and
                // ModelState full of spurious required-field errors. Clear them (tag helpers
                // render input values from ModelState in preference to the model), keep only
                // the transition error, and rebuild Form from the stored event.
                ModelState.Clear();
                ModelState.AddModelError(string.Empty, "Transição de estado inválida.");
                var reload = await ReloadAsync(id, ct);
                if (Event is not null)
                    PopulateFormFrom(Event);
                return reload;
            default:
                TempData["Success"] = successMessage;
                return RedirectToPage(new { id });
        }
    }

    private async Task<IActionResult> ReloadAsync(Guid id, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(id, ct);
        return Event is null ? NotFound() : Page();
    }

    private void PopulateFormFrom(Event evt)
        => Form = new EventForm
        {
            Name = evt.Name,
            Description = evt.Description,
            Date = evt.Date,
            Venue = evt.Venue,
            City = evt.City,
            TicketsUrl = evt.TicketsUrl,
            StreamUrl = evt.StreamUrl,
            Slug = evt.Slug,
        };
}
