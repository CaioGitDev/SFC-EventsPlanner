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

// Enum-name fields (method, status, billing, discipline) are deliberately raw English
// codes: the portal maps them to pt-PT labels, icons, and colors. Only
// PublicFighterFightRow.Summary ships pre-built pt-PT text — it is a sentence,
// not a code. Do not "fix" one to match the other.
public record PublicResultInfo(string? WinnerCorner, string Method, int? Round, string? Time);

public record PublicFightResultRow(int Order, string Billing, string Discipline,
    string? WeightClass, decimal? CatchweightKg, bool IsTitleFight, bool IsAmateur,
    PublicCardAthlete Red, PublicCardAthlete Blue, string Status, PublicResultInfo? Result);

public record PublicWeighInRow(int Order, string AthleteName, string? AthleteSlug, string Corner,
    string? WeightClass, decimal? CatchweightKg, decimal? OfficialWeightKg, DateTime? WeighedAt,
    bool MissedWeight);

public record PublicFighterFightRow(string EventName, string EventSlug, DateTime EventDate,
    string OpponentName, string? OpponentSlug, string Summary);

public record PublicUpcomingFight(string EventName, string EventSlug, DateTime EventDate,
    string OpponentName, string? OpponentSlug);

public record PublicFighterProfile(string Name, string? Nickname, string Slug, string? PhotoUrl,
    string Nationality, int Age, string Discipline, string Status, string? ClubName,
    string Record, int WinsByKo, List<PublicFighterFightRow> LastFights,
    PublicUpcomingFight? NextFight);

public class PublicContentService(SfcDbContext db)
{
    // Public = one of these statuses AND published at least once. Without the
    // PublishedAt check, cancelling a never-announced draft (a routine backoffice
    // action) would put its whole card on the internet.
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
            .Where(e => PublicStatuses.Contains(e.Status) && e.PublishedAt != null)
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

    /// <summary>Null when missing, inactive, or without public-profile consent (ADR-004 → 404).</summary>
    public async Task<PublicFighterProfile?> GetFighterAsync(string slug, CancellationToken ct = default)
    {
        var athlete = await db.Athletes.AsNoTracking()
            .Include(a => a.Club)
            .SingleOrDefaultAsync(a => a.Slug == slug && a.IsActive && a.PublicProfileConsent, ct);
        if (athlete is null)
            return null;

        var today = PortugalTime.Today.ToDateTime(TimeOnly.MinValue);
        var fights = await db.Fights.AsNoTracking()
            .Include(f => f.Result)
            .Include(f => f.RedCornerAthlete)
            .Include(f => f.BlueCornerAthlete)
            .Where(f => f.RedCornerAthleteId == athlete.Id || f.BlueCornerAthleteId == athlete.Id)
            .Join(db.Events.AsNoTracking()
                    .Where(e => PublicStatuses.Contains(e.Status) && e.PublishedAt != null),
                f => f.EventId, e => e.Id, (f, e) => new { Fight = f, Event = e })
            .ToListAsync(ct);

        var lastFights = fights
            .Where(x => x.Fight.Result is not null)
            .OrderByDescending(x => x.Event.Date)
            .Take(5)
            .Select(x => new PublicFighterFightRow(x.Event.Name, x.Event.Slug, x.Event.Date,
                OpponentOf(x.Fight, athlete.Id).Name, OpponentOf(x.Fight, athlete.Id).Slug,
                ResultSummaryFor(x.Fight, athlete.Id)))
            .ToList();

        var next = fights
            .Where(x => x.Fight.Status == FightStatus.Scheduled
                && x.Event.Status == EventStatus.Published && x.Event.Date >= today)
            .OrderBy(x => x.Event.Date)
            .Select(x => new PublicUpcomingFight(x.Event.Name, x.Event.Slug, x.Event.Date,
                OpponentOf(x.Fight, athlete.Id).Name, OpponentOf(x.Fight, athlete.Id).Slug))
            .FirstOrDefault();

        return new PublicFighterProfile($"{athlete.FirstName} {athlete.LastName}",
            athlete.Nickname, athlete.Slug, athlete.PhotoUrl, athlete.Nationality, athlete.Age,
            athlete.Discipline.ToString(), athlete.Status.ToString(), athlete.Club?.Name,
            athlete.RecordDisplay, athlete.WinsByKo, lastFights, next);
    }

    public async Task<List<PublicFightResultRow>?> GetEventResultsAsync(string slug,
        CancellationToken ct = default)
    {
        var evt = await QueryPublicEventWithCard(slug).SingleOrDefaultAsync(ct);
        if (evt is null)
            return null;

        return evt.Fights.Select(fight => new PublicFightResultRow(
                fight.Order, fight.Billing.ToString(), fight.Discipline.ToString(),
                fight.WeightClass, fight.CatchweightKg, fight.IsTitleFight, fight.IsAmateur,
                PublicCardAthlete.From(fight.RedCornerAthlete),
                PublicCardAthlete.From(fight.BlueCornerAthlete),
                fight.Status.ToString(),
                fight.Result is null
                    ? null
                    : new PublicResultInfo(
                        fight.Result.WinnerAthleteId is null
                            ? null
                            : fight.Result.WinnerAthleteId == fight.RedCornerAthleteId ? "red" : "blue",
                        fight.Result.Method.ToString(),
                        fight.Result.Round,
                        fight.Result.Time)))
            .ToList();
    }

    /// <summary>Approved weigh-ins only — approval is publication (ADR-004).</summary>
    public async Task<List<PublicWeighInRow>?> GetEventWeighInsAsync(string slug,
        CancellationToken ct = default)
    {
        var evt = await QueryPublicEventWithCard(slug).SingleOrDefaultAsync(ct);
        if (evt is null)
            return null;

        var fightIds = evt.Fights.Select(f => f.Id).ToList();
        var weighIns = await db.WeighIns.AsNoTracking()
            .Where(w => fightIds.Contains(w.FightId) && w.IsApproved)
            .ToListAsync(ct);

        var rows = new List<PublicWeighInRow>();
        foreach (var fight in evt.Fights)
        {
            foreach (var corner in new[] { Corner.Red, Corner.Blue })
            {
                var athleteId = corner == Corner.Red ? fight.RedCornerAthleteId : fight.BlueCornerAthleteId;
                var weighIn = weighIns.SingleOrDefault(
                    w => w.FightId == fight.Id && w.AthleteId == athleteId);
                if (weighIn is null)
                    continue;

                var athlete = corner == Corner.Red ? fight.RedCornerAthlete : fight.BlueCornerAthlete;
                var card = PublicCardAthlete.From(athlete);
                rows.Add(new PublicWeighInRow(fight.Order, card.Name, card.Slug,
                    corner.ToString().ToLowerInvariant(), fight.WeightClass, fight.CatchweightKg,
                    weighIn.OfficialWeightKg, weighIn.WeighedAt,
                    weighIn.IsOverweight(fight.WeightLimitKg)));
            }
        }

        return rows;
    }

    private static PublicCardAthlete OpponentOf(Fight fight, Guid athleteId)
        => PublicCardAthlete.From(fight.RedCornerAthleteId == athleteId
            ? fight.BlueCornerAthlete
            : fight.RedCornerAthlete);

    /// <summary>Result line from the athlete's perspective: "Vitória por KO (R2 1:11)", "Derrota…", "Empate", "No contest".</summary>
    private static string ResultSummaryFor(Fight fight, Guid athleteId)
    {
        var result = fight.Result!;
        if (result.Method == FightResultMethod.Draw)
            return "Empate";
        if (result.Method == FightResultMethod.NoContest)
            return "No contest";

        var outcome = result.WinnerAthleteId == athleteId ? "Vitória" : "Derrota";
        var summary = $"{outcome} por {result.Method.ToDisplay()}";
        if (result.Round is not null)
            summary += $" (R{result.Round}{(result.Time is null ? "" : $" {result.Time}")})";
        return summary;
    }

    internal IQueryable<Event> QueryPublicEventWithCard(string slug)
        => db.Events.AsNoTracking()
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.RedCornerAthlete)
                .ThenInclude(a => a!.Club)
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.BlueCornerAthlete)
                .ThenInclude(a => a!.Club)
            .Include(e => e.Fights.OrderBy(f => f.Order)).ThenInclude(f => f.Result)
            .Where(e => e.Slug == slug && PublicStatuses.Contains(e.Status)
                && e.PublishedAt != null);

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
