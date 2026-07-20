using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Common;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Images;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Services;

public record EventInput(string Name, string? Description, DateTime Date, string? Venue,
    string? City, string? TicketsUrl, string? StreamUrl, string? Slug);

public record EventListItem(Guid Id, string Name, DateTime Date, string? City,
    EventStatus Status, int FightCount);

public enum EventDeleteResult
{
    Deleted,
    NotFound,
    NotDeletable,
    HasResults,
}

public enum EventTransitionResult
{
    Success,
    NotFound,
    InvalidTransition,
}

public record FightInput(Guid RedCornerAthleteId, Guid BlueCornerAthleteId,
    Sfc.Domain.Athletes.Discipline Discipline, int Rounds, int RoundDurationMinutes,
    string? WeightClass, decimal? CatchweightKg, bool IsTitleFight, bool IsAmateur);

public enum CardOperationResult
{
    Success,
    EventNotFound,
    FightNotFound,
    AthleteAlreadyInCard,
    SameAthleteBothCorners,
    FightNotScheduled,
    EventLocked,
    HasResult,
}

public record ResultInput(Guid? WinnerAthleteId, FightResultMethod Method, int? Round, string? Time);

public enum ResultOperationResult
{
    Success,
    EventNotFound,
    FightNotFound,
    EventCancelled,
    EventNotYetHeld,
    FightNotScheduled,
    HasNoResult,
    InvalidInput,
    ConcurrencyConflict,
}

public class EventService(SfcDbContext db, IImageStorage imageStorage)
{
    private const int BannerMaxDimension = 1920;
    private const int PosterMaxDimension = 1200;

    public async Task<List<EventListItem>> SearchAsync(string? name, EventStatus? status,
        CancellationToken ct = default)
    {
        var query = db.Events.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(e => EF.Functions.ILike(e.Name, $"%{name.Trim()}%"));

        if (status is not null)
            query = query.Where(e => e.Status == status);

        return await query
            .OrderByDescending(e => e.Date)
            .Select(e => new EventListItem(e.Id, e.Name, e.Date, e.City, e.Status, e.Fights.Count))
            .ToListAsync(ct);
    }

    public Task<Event?> GetWithCardAsync(Guid id, CancellationToken ct = default)
        => db.Events
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.RedCornerAthlete)
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.BlueCornerAthlete)
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.Result)
            .SingleOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Event> CreateAsync(EventInput input, Stream? banner, Stream? poster,
        CancellationToken ct = default)
    {
        var slug = await ResolveSlugAsync(input, excludeId: null, ct);
        var evt = new Event(db.CurrentOrganizationId, input.Name, input.Date, slug,
            input.Description, input.Venue, input.City, input.TicketsUrl, input.StreamUrl);

        await UploadImagesAsync(evt, banner, poster, ct);

        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);
        return evt;
    }

    public async Task<Event?> UpdateAsync(Guid id, EventInput input, Stream? banner,
        Stream? poster, CancellationToken ct = default)
    {
        var evt = await db.Events.Include(e => e.Fights).SingleOrDefaultAsync(e => e.Id == id, ct);
        if (evt is null)
            return null;

        evt.Update(input.Name, input.Description, input.Date, input.Venue, input.City,
            input.TicketsUrl, input.StreamUrl);

        if (evt.PublishedAt is null)
        {
            var slug = await ResolveSlugAsync(input, excludeId: id, ct);
            if (slug != evt.Slug)
                evt.UpdateSlug(slug);
        }

        await UploadImagesAsync(evt, banner, poster, ct);
        await db.SaveChangesAsync(ct);
        return evt;
    }

    public Task<EventTransitionResult> PublishAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, e => e.Publish(), ct);

    public Task<EventTransitionResult> UnpublishAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, e => e.Unpublish(), ct);

    public Task<EventTransitionResult> CompleteAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, e => e.Complete(), ct);

    public Task<EventTransitionResult> CancelAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, e => e.Cancel(), ct);

    public async Task<EventDeleteResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var evt = await db.Events.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (evt is null)
            return EventDeleteResult.NotFound;

        if (evt.Status is not (EventStatus.Draft or EventStatus.Cancelled))
            return EventDeleteResult.NotDeletable;

        // The cascade would silently drop results whose deltas are already applied
        // to athlete records (domain rule 6) — results must be deleted first.
        if (await db.Fights.AnyAsync(f => f.EventId == id && f.Result != null, ct))
            return EventDeleteResult.HasResults;

        db.Events.Remove(evt);
        await db.SaveChangesAsync(ct);

        if (evt.BannerUrl is not null)
            await imageStorage.DeleteAsync($"events/{evt.Id}-banner.webp", ct);
        if (evt.PosterUrl is not null)
            await imageStorage.DeleteAsync($"events/{evt.Id}-poster.webp", ct);

        return EventDeleteResult.Deleted;
    }

    public async Task<CardOperationResult> AddFightAsync(Guid eventId, FightInput input,
        CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;
        if (IsCardLocked(evt))
            return CardOperationResult.EventLocked;
        if (input.RedCornerAthleteId == input.BlueCornerAthleteId)
            return CardOperationResult.SameAthleteBothCorners;
        if (evt.HasAthlete(input.RedCornerAthleteId) || evt.HasAthlete(input.BlueCornerAthleteId))
            return CardOperationResult.AthleteAlreadyInCard;

        var fight = evt.AddFight(input.RedCornerAthleteId, input.BlueCornerAthleteId, input.Discipline,
            input.Rounds, input.RoundDurationMinutes, input.WeightClass, input.CatchweightKg,
            input.IsTitleFight, input.IsAmateur);
        // Event.AddFight only mutates the aggregate's private collection; the new Fight's
        // client-generated Guid key would otherwise make EF's change tracker mistake it for
        // an existing (Modified) row instead of a new (Added) one, so it must be attached explicitly.
        db.Fights.Add(fight);
        await db.SaveChangesAsync(ct);
        return CardOperationResult.Success;
    }

    public async Task<CardOperationResult> RemoveFightAsync(Guid eventId, Guid fightId,
        CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;
        if (IsCardLocked(evt))
            return CardOperationResult.EventLocked;

        var fight = evt.Fights.SingleOrDefault(f => f.Id == fightId);
        if (fight is null)
            return CardOperationResult.FightNotFound;
        if (fight.Result is not null)
            return CardOperationResult.HasResult;

        evt.RemoveFight(fightId);
        await db.SaveChangesAsync(ct);
        return CardOperationResult.Success;
    }

    public async Task<CardOperationResult> MoveFightAsync(Guid eventId, Guid fightId,
        MoveDirection direction, CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;
        if (IsCardLocked(evt))
            return CardOperationResult.EventLocked;
        if (evt.Fights.All(f => f.Id != fightId))
            return CardOperationResult.FightNotFound;

        evt.MoveFight(fightId, direction);
        await db.SaveChangesAsync(ct);
        return CardOperationResult.Success;
    }

    public async Task<CardOperationResult> ReplaceAthleteAsync(Guid eventId, Guid fightId,
        Corner corner, Guid newAthleteId, CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;
        if (IsCardLocked(evt))
            return CardOperationResult.EventLocked;

        var fight = evt.Fights.SingleOrDefault(f => f.Id == fightId);
        if (fight is null)
            return CardOperationResult.FightNotFound;
        if (fight.Status != FightStatus.Scheduled)
            return CardOperationResult.FightNotScheduled;
        if (evt.HasAthlete(newAthleteId))
            return CardOperationResult.AthleteAlreadyInCard;

        evt.ReplaceAthlete(fightId, corner, newAthleteId);
        await db.SaveChangesAsync(ct);
        return CardOperationResult.Success;
    }

    private Task<Event?> GetTrackedWithFightsAsync(Guid id, CancellationToken ct)
        => db.Events.Include(e => e.Fights).ThenInclude(f => f.Result)
            .SingleOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>
    /// Records the fight's result, or corrects the existing one by reverting the old
    /// deltas and applying the new ones. Record updates for both athletes happen in the
    /// same SaveChanges (one transaction).
    /// </summary>
    public async Task<ResultOperationResult> SaveResultAsync(Guid eventId, Guid fightId,
        ResultInput input, CancellationToken ct = default)
    {
        var (evt, fight, notFound) = await LoadFightAsync(eventId, fightId, ct);
        if (notFound is not null)
            return notFound.Value;

        var athletes = await LoadCornerAthletesAsync(fight!, ct);
        var red = athletes[fight!.RedCornerAthleteId];
        var blue = athletes[fight.BlueCornerAthleteId];

        try
        {
            if (fight.Result is null)
            {
                if (evt!.Status == EventStatus.Cancelled)
                    return ResultOperationResult.EventCancelled;
                if (DateOnly.FromDateTime(evt.Date) > TodayInPortugal())
                    return ResultOperationResult.EventNotYetHeld;
                if (fight.Status != FightStatus.Scheduled)
                    return ResultOperationResult.FightNotScheduled;

                var result = evt.RecordResult(fightId, input.WinnerAthleteId, input.Method,
                    input.Round, input.Time, TodayInPortugal());
                // Client-generated Guid key: attach explicitly so EF tracks it as Added.
                db.FightResults.Add(result);
                ApplyDeltas(result, fight, red, blue);
            }
            else
            {
                var oldDeltas = fight.Result.GetDeltas(fight.RedCornerAthleteId, fight.BlueCornerAthleteId);
                var result = evt!.ChangeResult(fightId, input.WinnerAthleteId, input.Method,
                    input.Round, input.Time);
                red.ApplyResultDelta(oldDeltas.Red.Negate());
                blue.ApplyResultDelta(oldDeltas.Blue.Negate());
                ApplyDeltas(result, fight, red, blue);
            }
        }
        catch (ArgumentException)
        {
            return ResultOperationResult.InvalidInput;
        }

        return await SaveResultChangesAsync(ct);
    }

    /// <summary>Reverts both athletes' deltas and removes the result; the fight returns to Scheduled.</summary>
    public async Task<ResultOperationResult> DeleteResultAsync(Guid eventId, Guid fightId,
        CancellationToken ct = default)
    {
        var (evt, fight, notFound) = await LoadFightAsync(eventId, fightId, ct);
        if (notFound is not null)
            return notFound.Value;
        if (fight!.Result is null)
            return ResultOperationResult.HasNoResult;

        var athletes = await LoadCornerAthletesAsync(fight, ct);
        var deltas = fight.Result.GetDeltas(fight.RedCornerAthleteId, fight.BlueCornerAthleteId);
        athletes[fight.RedCornerAthleteId].ApplyResultDelta(deltas.Red.Negate());
        athletes[fight.BlueCornerAthleteId].ApplyResultDelta(deltas.Blue.Negate());
        evt!.DeleteResult(fightId);

        return await SaveResultChangesAsync(ct);
    }

    /// <summary>
    /// Saves record-affecting changes, translating optimistic-concurrency failures
    /// (xmin token on Athlete/FightResult, or a concurrent first-time insert hitting
    /// the unique FightId index) into a friendly result instead of a raw 500 —
    /// double-taps on weak venue wi-fi are the norm, not the exception.
    /// </summary>
    private async Task<ResultOperationResult> SaveResultChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ResultOperationResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultOperationResult.ConcurrencyConflict;
        }
        catch (DbUpdateException)
        {
            return ResultOperationResult.ConcurrencyConflict;
        }
    }

    public Task<ResultOperationResult> CancelFightAsync(Guid eventId, Guid fightId,
        CancellationToken ct = default)
        => FightStatusOperationAsync(eventId, fightId, (evt, id) => evt.CancelFight(id), ct);

    public Task<ResultOperationResult> MarkFightNoContestAsync(Guid eventId, Guid fightId,
        CancellationToken ct = default)
        => FightStatusOperationAsync(eventId, fightId, (evt, id) => evt.MarkFightNoContest(id), ct);

    public Task<ResultOperationResult> ReinstateFightAsync(Guid eventId, Guid fightId,
        CancellationToken ct = default)
        => FightStatusOperationAsync(eventId, fightId, (evt, id) => evt.ReinstateFight(id), ct);

    private async Task<ResultOperationResult> FightStatusOperationAsync(Guid eventId, Guid fightId,
        Action<Event, Guid> operation, CancellationToken ct)
    {
        var (evt, _, notFound) = await LoadFightAsync(eventId, fightId, ct);
        if (notFound is not null)
            return notFound.Value;

        try
        {
            operation(evt!, fightId);
        }
        catch (InvalidOperationException)
        {
            return ResultOperationResult.FightNotScheduled;
        }

        await db.SaveChangesAsync(ct);
        return ResultOperationResult.Success;
    }

    private async Task<(Event? Event, Fight? Fight, ResultOperationResult? NotFound)> LoadFightAsync(
        Guid eventId, Guid fightId, CancellationToken ct)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return (null, null, ResultOperationResult.EventNotFound);

        var fight = evt.Fights.SingleOrDefault(f => f.Id == fightId);
        return fight is null
            ? (evt, null, ResultOperationResult.FightNotFound)
            : (evt, fight, null);
    }

    private async Task<Dictionary<Guid, Sfc.Domain.Athletes.Athlete>> LoadCornerAthletesAsync(
        Fight fight, CancellationToken ct)
    {
        var ids = new[] { fight.RedCornerAthleteId, fight.BlueCornerAthleteId };
        return await db.Athletes.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
    }

    private static void ApplyDeltas(FightResult result, Fight fight,
        Sfc.Domain.Athletes.Athlete red, Sfc.Domain.Athletes.Athlete blue)
    {
        var deltas = result.GetDeltas(fight.RedCornerAthleteId, fight.BlueCornerAthleteId);
        red.ApplyResultDelta(deltas.Red);
        blue.ApplyResultDelta(deltas.Blue);
    }

    private static DateOnly TodayInPortugal()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }

    private static bool IsCardLocked(Event evt)
        => evt.Status is EventStatus.Completed or EventStatus.Cancelled;

    private async Task<EventTransitionResult> TransitionAsync(Guid id, Action<Event> transition,
        CancellationToken ct)
    {
        var evt = await db.Events.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (evt is null)
            return EventTransitionResult.NotFound;

        try
        {
            transition(evt);
        }
        catch (InvalidOperationException)
        {
            return EventTransitionResult.InvalidTransition;
        }

        await db.SaveChangesAsync(ct);
        return EventTransitionResult.Success;
    }

    private async Task UploadImagesAsync(Event evt, Stream? banner, Stream? poster,
        CancellationToken ct)
    {
        if (banner is not null)
        {
            using var webp = await ImageProcessor.ToWebpAsync(banner, BannerMaxDimension, ct);
            evt.SetBanner(await imageStorage.SaveAsync(webp, $"events/{evt.Id}-banner.webp", "image/webp", ct));
        }

        if (poster is not null)
        {
            using var webp = await ImageProcessor.ToWebpAsync(poster, PosterMaxDimension, ct);
            evt.SetPoster(await imageStorage.SaveAsync(webp, $"events/{evt.Id}-poster.webp", "image/webp", ct));
        }
    }

    private async Task<string> ResolveSlugAsync(EventInput input, Guid? excludeId,
        CancellationToken ct)
    {
        var baseSlug = SlugGenerator.Generate(
            string.IsNullOrWhiteSpace(input.Slug) ? input.Name : input.Slug);

        var slug = baseSlug;
        var suffix = 2;
        while (await db.Events.AnyAsync(
            e => e.Slug == slug && (excludeId == null || e.Id != excludeId), ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }
}
