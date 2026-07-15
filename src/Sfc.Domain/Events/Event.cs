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
