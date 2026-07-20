using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;

namespace Sfc.Web.Services;

// Public DTOs: slugs are the only keys — never expose GUIDs, DateOfBirth (age only),
// contacts, Draft events, or unapproved weigh-in weights (ADR-004 + prompt 05).

public record PublicEventSummary(string Name, string Slug, DateTime Date, string? Venue,
    string? City, string Status, string? BannerUrl, string? PosterUrl, string? TicketsUrl,
    string? StreamUrl, int FightCount);

public record PublicEventsList(List<PublicEventSummary> Upcoming, List<PublicEventSummary> Past);

public record PublicEventDetail(string Name, string Slug, DateTime Date, string? Venue,
    string? City, string Status, string? Description, string? BannerUrl, string? PosterUrl,
    string? TicketsUrl, string? StreamUrl, List<PublicFightCardEntry> Fights);

public record PublicFightCardEntry(int Order, string Billing, string Discipline, int Rounds,
    int RoundDurationMinutes, string? WeightClass, decimal? CatchweightKg, bool IsTitleFight,
    bool IsAmateur, string Status, PublicCardAthlete Red, PublicCardAthlete Blue);

/// <summary>
/// Consent redaction lives in exactly one place: without <see cref="Athlete.PublicProfileConsent"/>
/// only the name is exposed (ADR-004).
/// </summary>
public record PublicCardAthlete(string Name, string? Nickname, string? Slug, string? PhotoUrl,
    string? Nationality, int? Age, string? Record, string? ClubName)
{
    public static PublicCardAthlete From(Athlete? athlete)
    {
        if (athlete is null)
            return new PublicCardAthlete("?", null, null, null, null, null, null, null);

        var name = $"{athlete.FirstName} {athlete.LastName}";
        if (!athlete.PublicProfileConsent)
            return new PublicCardAthlete(name, null, null, null, null, null, null, null);

        return new PublicCardAthlete(name, athlete.Nickname, athlete.Slug, athlete.PhotoUrl,
            athlete.Nationality, athlete.Age, athlete.RecordDisplay, athlete.Club?.Name);
    }
}

public class PublicContentService(SfcDbContext db)
{
    private static readonly EventStatus[] PublicStatuses =
        [EventStatus.Published, EventStatus.Completed, EventStatus.Cancelled];

    public async Task<PublicEventSummary?> GetNextEventAsync(CancellationToken ct = default)
    {
        var today = PortugalTime.Today.ToDateTime(TimeOnly.MinValue);
        var evt = await db.Events.AsNoTracking()
            .Where(e => e.Status == EventStatus.Published && e.Date >= today)
            .OrderBy(e => e.Date)
            .FirstOrDefaultAsync(ct);
        return evt is null ? null : await ToSummaryAsync(evt, ct);
    }

    public async Task<PublicEventsList> GetEventsAsync(CancellationToken ct = default)
    {
        var today = PortugalTime.Today.ToDateTime(TimeOnly.MinValue);
        var events = await db.Events.AsNoTracking()
            .Where(e => PublicStatuses.Contains(e.Status))
            .Select(e => new { Event = e, FightCount = e.Fights.Count })
            .ToListAsync(ct);

        var summaries = events
            .Select(x => ToSummary(x.Event, x.FightCount))
            .ToList();

        return new PublicEventsList(
            summaries.Where(s => s.Date >= today).OrderBy(s => s.Date).ToList(),
            summaries.Where(s => s.Date < today).OrderByDescending(s => s.Date).ToList());
    }

    public async Task<PublicEventDetail?> GetEventAsync(string slug, CancellationToken ct = default)
    {
        var evt = await QueryPublicEventWithCard(slug).SingleOrDefaultAsync(ct);
        if (evt is null)
            return null;

        var fights = evt.Fights.Select(ToCardEntry).ToList();
        return new PublicEventDetail(evt.Name, evt.Slug, evt.Date, evt.Venue, evt.City,
            evt.Status.ToString(), evt.Description, evt.BannerUrl, evt.PosterUrl,
            evt.TicketsUrl, evt.StreamUrl, fights);
    }

    internal IQueryable<Event> QueryPublicEventWithCard(string slug)
        => db.Events.AsNoTracking()
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.RedCornerAthlete)
                .ThenInclude(a => a!.Club)
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.BlueCornerAthlete)
                .ThenInclude(a => a!.Club)
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.Result)
            .Where(e => e.Slug == slug && PublicStatuses.Contains(e.Status));

    internal static PublicFightCardEntry ToCardEntry(Fight fight)
        => new(fight.Order, fight.Billing.ToString(), fight.Discipline.ToString(),
            fight.Rounds, fight.RoundDurationMinutes, fight.WeightClass, fight.CatchweightKg,
            fight.IsTitleFight, fight.IsAmateur, fight.Status.ToString(),
            PublicCardAthlete.From(fight.RedCornerAthlete),
            PublicCardAthlete.From(fight.BlueCornerAthlete));

    private async Task<PublicEventSummary> ToSummaryAsync(Event evt, CancellationToken ct)
        => ToSummary(evt, await db.Fights.CountAsync(f => f.EventId == evt.Id, ct));

    private static PublicEventSummary ToSummary(Event evt, int fightCount)
        => new(evt.Name, evt.Slug, evt.Date, evt.Venue, evt.City, evt.Status.ToString(),
            evt.BannerUrl, evt.PosterUrl, evt.TicketsUrl, evt.StreamUrl, fightCount);
}
