using Sfc.Domain.Athletes;
using Sfc.Domain.Common;

namespace Sfc.Domain.Events;

public class Event : IOrganizationScoped
{
    private readonly List<Fight> _fights = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Naive local date-time (timestamp without time zone) — see design decision.</summary>
    public DateTime Date { get; private set; }

    public string? Venue { get; private set; }
    public string? City { get; private set; }
    public string? BannerUrl { get; private set; }
    public string? PosterUrl { get; private set; }
    public EventStatus Status { get; private set; }

    /// <summary>Set on first publication; anchors slug immutability (domain rule 7).</summary>
    public DateTime? PublishedAt { get; private set; }

    public string? TicketsUrl { get; private set; }
    public string? StreamUrl { get; private set; }

    /// <summary>Fight card ordered by <see cref="Fight.Order"/> (1 opens the event; last is the main event).</summary>
    public IReadOnlyList<Fight> Fights => _fights.OrderBy(f => f.Order).ToList();

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Event()
    {
        Name = null!;
        Slug = null!;
    }

    public Event(Guid organizationId, string name, DateTime date, string slug,
        string? description = null, string? venue = null, string? city = null,
        string? ticketsUrl = null, string? streamUrl = null)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId is required.", nameof(organizationId));

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Name = null!;
        Slug = null!;
        SetDetails(name, description, date, venue, city, ticketsUrl, streamUrl);
        ChangeSlug(slug);
        Status = EventStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void Update(string name, string? description, DateTime date, string? venue,
        string? city, string? ticketsUrl, string? streamUrl)
    {
        SetDetails(name, description, date, venue, city, ticketsUrl, streamUrl);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Editable only until first publication (domain rule 7).</summary>
    public void UpdateSlug(string slug)
    {
        if (PublishedAt is not null)
            throw new InvalidOperationException("Slug is immutable after first publication.");

        ChangeSlug(slug);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Publish()
    {
        if (Status != EventStatus.Draft)
            throw new InvalidOperationException("Only draft events can be published.");

        Status = EventStatus.Published;
        PublishedAt ??= DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unpublish()
    {
        if (Status != EventStatus.Published)
            throw new InvalidOperationException("Only published events can be unpublished.");

        Status = EventStatus.Draft;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        if (Status != EventStatus.Published)
            throw new InvalidOperationException("Only published events can be completed.");

        Status = EventStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status is EventStatus.Completed or EventStatus.Cancelled)
            throw new InvalidOperationException("Completed or cancelled events cannot be cancelled.");

        Status = EventStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetBanner(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Banner URL is required.", nameof(url));

        BannerUrl = url.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPoster(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Poster URL is required.", nameof(url));

        PosterUrl = url.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public Fight AddFight(Guid redCornerAthleteId, Guid blueCornerAthleteId,
        Discipline discipline, int rounds, int roundDurationMinutes,
        string? weightClass, decimal? catchweightKg, bool isTitleFight, bool isAmateur)
    {
        EnsureCardEditable();
        EnsureAthleteNotInCard(redCornerAthleteId);
        EnsureAthleteNotInCard(blueCornerAthleteId);

        var fight = new Fight(OrganizationId, Id, _fights.Count + 1,
            redCornerAthleteId, blueCornerAthleteId, discipline, rounds, roundDurationMinutes,
            weightClass, catchweightKg, isTitleFight, isAmateur);
        _fights.Add(fight);
        RecalculateBilling();
        UpdatedAt = DateTime.UtcNow;
        return fight;
    }

    public bool RemoveFight(Guid fightId)
    {
        EnsureCardEditable();
        var fight = _fights.SingleOrDefault(f => f.Id == fightId);
        if (fight is null)
            return false;

        _fights.Remove(fight);
        foreach (var later in _fights.Where(f => f.Order > fight.Order))
            later.SetOrder(later.Order - 1);

        RecalculateBilling();
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Up = earlier in the night (lower Order); Down = towards the main event.</summary>
    public bool MoveFight(Guid fightId, MoveDirection direction)
    {
        EnsureCardEditable();
        var fight = FindFight(fightId);
        var targetOrder = direction == MoveDirection.Up ? fight.Order - 1 : fight.Order + 1;
        var neighbour = _fights.SingleOrDefault(f => f.Order == targetOrder);
        if (neighbour is null)
            return false;

        neighbour.SetOrder(fight.Order);
        fight.SetOrder(targetOrder);
        RecalculateBilling();
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public void ReplaceAthlete(Guid fightId, Corner corner, Guid newAthleteId)
    {
        EnsureCardEditable();
        var fight = FindFight(fightId);
        EnsureAthleteNotInCard(newAthleteId);
        fight.ReplaceCorner(corner, newAthleteId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records the result of a scheduled fight. Only once the event date has arrived
    /// (domain rule 5) and never on a cancelled event. Result operations bypass
    /// <see cref="EnsureCardEditable"/> — they are not card changes.
    /// </summary>
    public FightResult RecordResult(Guid fightId, Guid? winnerAthleteId,
        FightResultMethod method, int? round, string? time, DateOnly today)
    {
        if (Status == EventStatus.Cancelled)
            throw new InvalidOperationException("Results cannot be recorded on a cancelled event.");
        if (DateOnly.FromDateTime(Date) > today)
            throw new InvalidOperationException("Results can only be recorded once the event date has arrived.");

        var fight = FindFight(fightId);
        var result = fight.RecordResult(winnerAthleteId, method, round, time);
        UpdatedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>Corrects an existing result; the caller reverts the old deltas and applies the new ones.</summary>
    public FightResult ChangeResult(Guid fightId, Guid? winnerAthleteId,
        FightResultMethod method, int? round, string? time)
    {
        var fight = FindFight(fightId);
        var result = fight.ChangeResult(winnerAthleteId, method, round, time);
        UpdatedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>Deletes the result; the fight returns to Scheduled (domain rule 6 — revert first).</summary>
    public void DeleteResult(Guid fightId)
    {
        FindFight(fightId).DeleteResult();
        UpdatedAt = DateTime.UtcNow;
    }

    public void CancelFight(Guid fightId)
    {
        FindFight(fightId).Cancel();
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFightNoContest(Guid fightId)
    {
        FindFight(fightId).MarkNoContest();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Recovers a cancelled/no-contest fight without result back to Scheduled (event-day mis-taps).</summary>
    public void ReinstateFight(Guid fightId)
    {
        FindFight(fightId).Reinstate();
        UpdatedAt = DateTime.UtcNow;
    }

    public bool HasAthlete(Guid athleteId)
        => _fights.Any(f => f.RedCornerAthleteId == athleteId || f.BlueCornerAthleteId == athleteId);

    private Fight FindFight(Guid fightId)
        => _fights.SingleOrDefault(f => f.Id == fightId)
            ?? throw new InvalidOperationException("Fight not found in this event.");

    private void EnsureCardEditable()
    {
        if (Status is EventStatus.Completed or EventStatus.Cancelled)
            throw new InvalidOperationException(
                "The card of a completed or cancelled event cannot be changed.");
    }

    private void EnsureAthleteNotInCard(Guid athleteId)
    {
        if (HasAthlete(athleteId))
            throw new InvalidOperationException("Athlete already has a fight in this event.");
    }

    private void RecalculateBilling()
    {
        var ordered = _fights.OrderBy(f => f.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var billing = i == ordered.Count - 1 ? FightBilling.Main
                : i == ordered.Count - 2 ? FightBilling.CoMain
                : FightBilling.Card;
            ordered[i].SetBilling(billing);
        }
    }

    private void SetDetails(string name, string? description, DateTime date, string? venue,
        string? city, string? ticketsUrl, string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (date == default)
            throw new ArgumentException("Date is required.", nameof(date));

        Name = name.Trim();
        Description = NullIfBlank(description);
        Date = date;
        Venue = NullIfBlank(venue);
        City = NullIfBlank(city);
        TicketsUrl = NullIfBlank(ticketsUrl);
        StreamUrl = NullIfBlank(streamUrl);
    }

    private void ChangeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug != SlugGenerator.Generate(slug))
            throw new ArgumentException("Slug must be in canonical form.", nameof(slug));

        Slug = slug;
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
