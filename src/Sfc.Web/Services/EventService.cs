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
}

public enum EventTransitionResult
{
    Success,
    NotFound,
    InvalidTransition,
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

        db.Events.Remove(evt);
        await db.SaveChangesAsync(ct);

        if (evt.BannerUrl is not null)
            await imageStorage.DeleteAsync($"events/{evt.Id}-banner.webp", ct);
        if (evt.PosterUrl is not null)
            await imageStorage.DeleteAsync($"events/{evt.Id}-poster.webp", ct);

        return EventDeleteResult.Deleted;
    }

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
