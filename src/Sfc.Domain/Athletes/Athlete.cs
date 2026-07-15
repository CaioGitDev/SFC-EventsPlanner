using Sfc.Domain.Clubs;
using Sfc.Domain.Common;

namespace Sfc.Domain.Athletes;

public class Athlete : IOrganizationScoped
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string? Nickname { get; private set; }
    public string Slug { get; private set; }
    public string? PhotoUrl { get; private set; }
    public string Nationality { get; private set; }

    /// <summary>Never exposed publicly — the portal shows <see cref="Age"/> (ADR-004).</summary>
    public DateOnly DateOfBirth { get; private set; }

    public Guid? ClubId { get; private set; }
    public Club? Club { get; private set; }
    public string? CoachName { get; private set; }
    public Discipline Discipline { get; private set; }
    public string? WeightClass { get; private set; }
    public decimal? WeightKg { get; private set; }
    public int? HeightCm { get; private set; }
    public AthleteStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public bool PublicProfileConsent { get; private set; }

    /// <summary>
    /// Internal backoffice-only notes (e.g. guardian consent records for minors,
    /// ADR-004). NEVER exposed on the public portal.
    /// </summary>
    public string? Notes { get; private set; }

    // Historical record from before the platform. Set at creation, never
    // mutated (domain rule 3) — results from platform fights are aggregated
    // on top of these (prompt 03).
    public int BaselineWins { get; private set; }
    public int BaselineLosses { get; private set; }
    public int BaselineDraws { get; private set; }
    public int BaselineKos { get; private set; }

    public int Wins => BaselineWins;
    public int Losses => BaselineLosses;
    public int Draws => BaselineDraws;
    public int WinsByKo => BaselineKos;
    public string RecordDisplay => $"{Wins}-{Losses}-{Draws}";

    public int Age
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - DateOfBirth.Year;
            if (DateOfBirth > today.AddYears(-age))
                age--;
            return age;
        }
    }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Athlete()
    {
        FirstName = null!;
        LastName = null!;
        Slug = null!;
        Nationality = null!;
    }

    public Athlete(Guid organizationId, string firstName, string lastName, DateOnly dateOfBirth,
        string nationality, Discipline discipline, AthleteStatus status, string slug,
        string? nickname = null, Guid? clubId = null, string? coachName = null,
        string? weightClass = null, decimal? weightKg = null, int? heightCm = null,
        bool publicProfileConsent = false, string? notes = null,
        int baselineWins = 0, int baselineLosses = 0, int baselineDraws = 0, int baselineKos = 0)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("OrganizationId is required.", nameof(organizationId));
        if (baselineWins < 0)
            throw new ArgumentOutOfRangeException(nameof(baselineWins));
        if (baselineLosses < 0)
            throw new ArgumentOutOfRangeException(nameof(baselineLosses));
        if (baselineDraws < 0)
            throw new ArgumentOutOfRangeException(nameof(baselineDraws));
        if (baselineKos < 0)
            throw new ArgumentOutOfRangeException(nameof(baselineKos));
        if (baselineKos > baselineWins)
            throw new ArgumentException("KO wins cannot exceed total wins.", nameof(baselineKos));

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        FirstName = null!;
        LastName = null!;
        Slug = null!;
        Nationality = null!;
        SetProfile(firstName, lastName, nickname, dateOfBirth, nationality, discipline, status,
            clubId, coachName, weightClass, weightKg, heightCm, publicProfileConsent, notes);
        UpdateSlug(slug);
        BaselineWins = baselineWins;
        BaselineLosses = baselineLosses;
        BaselineDraws = baselineDraws;
        BaselineKos = baselineKos;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void Update(string firstName, string lastName, string? nickname, DateOnly dateOfBirth,
        string nationality, Discipline discipline, AthleteStatus status, Guid? clubId,
        string? coachName, string? weightClass, decimal? weightKg, int? heightCm,
        bool publicProfileConsent, bool isActive, string? notes)
    {
        SetProfile(firstName, lastName, nickname, dateOfBirth, nationality, discipline, status,
            clubId, coachName, weightClass, weightKg, heightCm, publicProfileConsent, notes);
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Editable throughout Fase 1 (design decision 2026-07-14); must be canonical.</summary>
    public void UpdateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug != SlugGenerator.Generate(slug))
            throw new ArgumentException("Slug must be in canonical form.", nameof(slug));

        Slug = slug;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPhoto(string photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl))
            throw new ArgumentException("Photo URL is required.", nameof(photoUrl));

        PhotoUrl = photoUrl.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    private void SetProfile(string firstName, string lastName, string? nickname,
        DateOnly dateOfBirth, string nationality, Discipline discipline, AthleteStatus status,
        Guid? clubId, string? coachName, string? weightClass, decimal? weightKg, int? heightCm,
        bool publicProfileConsent, string? notes)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required.", nameof(lastName));
        if (string.IsNullOrWhiteSpace(nationality))
            throw new ArgumentException("Nationality is required.", nameof(nationality));
        if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Date of birth cannot be in the future.", nameof(dateOfBirth));
        if (weightKg is <= 0)
            throw new ArgumentException("Weight must be positive.", nameof(weightKg));
        if (heightCm is <= 0)
            throw new ArgumentException("Height must be positive.", nameof(heightCm));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Nickname = NullIfBlank(nickname);
        DateOfBirth = dateOfBirth;
        Nationality = nationality.Trim();
        Discipline = discipline;
        Status = status;
        ClubId = clubId;
        CoachName = NullIfBlank(coachName);
        WeightClass = NullIfBlank(weightClass);
        WeightKg = weightKg;
        HeightCm = heightCm;
        PublicProfileConsent = publicProfileConsent;
        Notes = NullIfBlank(notes);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
