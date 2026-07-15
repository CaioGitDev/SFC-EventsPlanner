using Sfc.Domain.Common;

namespace Sfc.Domain.Clubs;

public class Club : IOrganizationScoped
{
    private readonly List<Coach> _coaches = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public IReadOnlyList<Coach> Coaches => _coaches.AsReadOnly();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Club()
    {
        Name = null!;
    }

    public Club(Guid organizationId, string name, string? city = null, string? country = null,
        string? contactEmail = null, string? contactPhone = null)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId is required.", nameof(organizationId));

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        Name = null!;
        SetDetails(name, city, country, contactEmail, contactPhone);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void Update(string name, string? city, string? country,
        string? contactEmail, string? contactPhone)
    {
        SetDetails(name, city, country, contactEmail, contactPhone);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetCoaches(IEnumerable<Coach> coaches)
    {
        _coaches.Clear();
        _coaches.AddRange(coaches);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLogo(string logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl))
            throw new ArgumentException("Logo URL is required.", nameof(logoUrl));

        LogoUrl = logoUrl.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    private void SetDetails(string name, string? city, string? country,
        string? contactEmail, string? contactPhone)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name.Trim();
        City = NullIfBlank(city);
        Country = NullIfBlank(country);
        ContactEmail = NullIfBlank(contactEmail);
        ContactPhone = NullIfBlank(contactPhone);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
