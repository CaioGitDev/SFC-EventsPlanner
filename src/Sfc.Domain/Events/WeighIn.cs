using Sfc.Domain.Common;

namespace Sfc.Domain.Events;

/// <summary>
/// One weigh-in per athlete per fight (unique). Standalone entity — it never mutates
/// fights, events, or records, so it lives outside the <see cref="Event"/> aggregate;
/// fight-context checks (athlete in corners, event not cancelled) happen in the service.
/// A weight miss is flagged, never blocking: the call (catchweight or cancellation) is human.
/// </summary>
public class WeighIn : IOrganizationScoped
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid FightId { get; private set; }
    public Guid AthleteId { get; private set; }

    /// <summary>Contractual weight; defaults to the fight's limit on first save.</summary>
    public decimal? ExpectedWeightKg { get; private set; }

    public decimal? OfficialWeightKg { get; private set; }
    public DateTime? WeighedAt { get; private set; }
    public bool IsApproved { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private WeighIn()
    {
    }

    public WeighIn(Guid organizationId, Guid fightId, Guid athleteId, decimal? expectedWeightKg)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId is required.", nameof(organizationId));
        if (fightId == Guid.Empty)
            throw new ArgumentException("Fight is required.", nameof(fightId));
        if (athleteId == Guid.Empty)
            throw new ArgumentException("Athlete is required.", nameof(athleteId));
        if (expectedWeightKg is <= 0)
            throw new ArgumentException("Expected weight must be positive.", nameof(expectedWeightKg));

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        FightId = fightId;
        AthleteId = athleteId;
        ExpectedWeightKg = expectedWeightKg;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Fat-finger guard ("710" for "71,0"); generous enough for kids and heavyweights.</summary>
    private const decimal MinPlausibleKg = 20m;
    private const decimal MaxPlausibleKg = 250m;

    public void RecordOfficialWeight(decimal officialWeightKg, DateTime weighedAtUtc)
    {
        if (officialWeightKg is < MinPlausibleKg or > MaxPlausibleKg)
            throw new ArgumentException(
                $"Official weight must be between {MinPlausibleKg} and {MaxPlausibleKg} kg.",
                nameof(officialWeightKg));

        OfficialWeightKg = officialWeightKg;
        WeighedAt = weighedAtUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Approve()
    {
        if (OfficialWeightKg is null)
            throw new InvalidOperationException("A weigh-in without an official weight cannot be approved.");

        IsApproved = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unapprove()
    {
        IsApproved = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetExpectedWeight(decimal? expectedWeightKg)
    {
        if (expectedWeightKg is <= 0)
            throw new ArgumentException("Expected weight must be positive.", nameof(expectedWeightKg));

        ExpectedWeightKg = expectedWeightKg;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Visual flag only — approving an overweight athlete stays possible.</summary>
    public bool IsOverweight(decimal? limitKg)
        => OfficialWeightKg is not null && limitKg is not null && OfficialWeightKg > limitKg;
}
