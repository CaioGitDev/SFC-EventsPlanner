# Clubs and Athletes Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full backoffice CRUD for Clubs and Athletes with photo/logo upload, search, and minimal cookie authentication, per `docs/plans/2026-07-14-atletas-clubes-design.md`.

**Architecture:** Rich domain entities in `Sfc.Domain` (invariants in constructors, no public setters on derived fields), EF Core persistence with the existing `IOrganizationScoped` query-filter convention, application services in `Sfc.Web/Services` orchestrating DbContext + image storage, thin Razor Pages under `Pages/Admin` protected by cookie auth.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, ASP.NET Identity (cookie), SixLabors.ImageSharp (WebP), AWSSDK.S3 3.7.* (MinIO dev / R2 prod), Bootstrap 5 local, xUnit + Testcontainers PostgreSQL.

## Global Constraints

- Code, entities, tests in **English**; all user-visible strings in **pt-PT** (CLAUDE.md rule 4).
- Every domain entity implements `IOrganizationScoped` with `OrganizationId` (ADR-002).
- `PublicProfileConsent` defaults to `false` (ADR-004).
- Baseline record fields set only in the `Athlete` constructor; never mutated afterwards (domain rule 3).
- Athlete slug: unique per `(OrganizationId, Slug)`, generated from name, editable throughout Fase 1.
- No repositories, no MediatR (ADR-001). Services + DbContext direct.
- Secrets never in code; MinIO dev defaults (`minioadmin`) are acceptable local-only values, same as the committed Postgres `sfc/sfc`.
- Never push to `master`; work happens on `feature/atletas-clubes` (already created).
- `TreatWarningsAsErrors` is on — code must compile warning-free.
- Working directory for all commands: repo root `D:\Users\160173003\Desktop\SFC-EventsPlanner`.

---

### Task 1: SlugGenerator (domain)

**Files:**
- Create: `src/Sfc.Domain/Common/SlugGenerator.cs`
- Test: `tests/Sfc.Domain.Tests/Common/SlugGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static string SlugGenerator.Generate(string input)` — throws `ArgumentException` on blank input or input with no alphanumeric characters. Used by `Athlete.UpdateSlug` validation (Task 3) and `AthleteService` (Task 10).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Domain.Tests/Common/SlugGeneratorTests.cs
using Sfc.Domain.Common;
using Xunit;

namespace Sfc.Domain.Tests.Common;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("João Peixão", "joao-peixao")]
    [InlineData("Ötzi Müller", "otzi-muller")]
    [InlineData("K1 Fighter!!!", "k1-fighter")]
    [InlineData("  Multiple   Spaces  ", "multiple-spaces")]
    [InlineData("UPPER-case", "upper-case")]
    [InlineData("a--b---c", "a-b-c")]
    public void Generate_ProducesLowercaseAsciiHyphenatedSlug(string input, string expected)
    {
        Assert.Equal(expected, SlugGenerator.Generate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_WithBlankInput_Throws(string? input)
    {
        Assert.Throws<ArgumentException>(() => SlugGenerator.Generate(input!));
    }

    [Fact]
    public void Generate_WithNoAlphanumericCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => SlugGenerator.Generate("!!! ---"));
    }

    [Fact]
    public void Generate_IsIdempotentOnItsOwnOutput()
    {
        var slug = SlugGenerator.Generate("João Peixão");
        Assert.Equal(slug, SlugGenerator.Generate(slug));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Domain.Tests --filter SlugGeneratorTests`
Expected: build error — `SlugGenerator` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Sfc.Domain/Common/SlugGenerator.cs
using System.Globalization;
using System.Text;

namespace Sfc.Domain.Common;

/// <summary>
/// Generates URL-safe slugs. Uniqueness is the caller's responsibility
/// (domain rule 7: unique per entity within the organization).
/// </summary>
public static class SlugGenerator
{
    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input is required.", nameof(input));

        var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        var slug = CollapseHyphens(builder.ToString());
        if (slug.Length == 0)
            throw new ArgumentException("Input contains no alphanumeric characters.", nameof(input));

        return slug;
    }

    private static string CollapseHyphens(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasHyphen = true; // trims leading hyphens
        foreach (var c in value)
        {
            if (c == '-')
            {
                if (!previousWasHyphen)
                    builder.Append('-');
                previousWasHyphen = true;
            }
            else
            {
                builder.Append(c);
                previousWasHyphen = false;
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Domain.Tests --filter SlugGeneratorTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Domain/Common/SlugGenerator.cs tests/Sfc.Domain.Tests/Common/SlugGeneratorTests.cs
git commit -m "Add slug generator"
```

---

### Task 2: Club entity and Coach value object (domain)

**Files:**
- Create: `src/Sfc.Domain/Clubs/Coach.cs`
- Create: `src/Sfc.Domain/Clubs/Club.cs`
- Test: `tests/Sfc.Domain.Tests/Clubs/ClubTests.cs`

**Interfaces:**
- Consumes: `IOrganizationScoped` (exists).
- Produces:
  - `Coach(string name, string? contact = null)` with `Name`, `Contact` get-only.
  - `Club(Guid organizationId, string name, string? city = null, string? country = null, string? contactEmail = null, string? contactPhone = null)`
  - `void Club.Update(string name, string? city, string? country, string? contactEmail, string? contactPhone)`
  - `void Club.SetCoaches(IEnumerable<Coach> coaches)`
  - `void Club.SetLogo(string logoUrl)`
  - `IReadOnlyList<Coach> Club.Coaches`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Domain.Tests/Clubs/ClubTests.cs
using Sfc.Domain.Clubs;
using Xunit;

namespace Sfc.Domain.Tests.Clubs;

public class ClubTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var club = new Club(OrgId, "Team Scorpion", "Lisboa", "Portugal", "geral@scorpion.pt", "+351 912 345 678");

        Assert.NotEqual(Guid.Empty, club.Id);
        Assert.Equal(OrgId, club.OrganizationId);
        Assert.Equal("Team Scorpion", club.Name);
        Assert.Equal("Lisboa", club.City);
        Assert.Equal("Portugal", club.Country);
        Assert.Equal("geral@scorpion.pt", club.ContactEmail);
        Assert.Equal("+351 912 345 678", club.ContactPhone);
        Assert.Empty(club.Coaches);
        Assert.Null(club.LogoUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Club(OrgId, name!));
    }

    [Fact]
    public void Constructor_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Club(Guid.Empty, "Team Scorpion"));
    }

    [Fact]
    public void Constructor_TrimsAndNormalizesOptionalFieldsToNull()
    {
        var club = new Club(OrgId, "  Team Scorpion  ", "  ", "", null, " +351 1 ");

        Assert.Equal("Team Scorpion", club.Name);
        Assert.Null(club.City);
        Assert.Null(club.Country);
        Assert.Null(club.ContactEmail);
        Assert.Equal("+351 1", club.ContactPhone);
    }

    [Fact]
    public void Update_ChangesDetails()
    {
        var club = new Club(OrgId, "Team Scorpion");

        club.Update("Scorpion Gym", "Porto", "Portugal", null, null);

        Assert.Equal("Scorpion Gym", club.Name);
        Assert.Equal("Porto", club.City);
    }

    [Fact]
    public void SetCoaches_ReplacesList()
    {
        var club = new Club(OrgId, "Team Scorpion");
        club.SetCoaches([new Coach("Mestre Rui", "rui@scorpion.pt")]);

        club.SetCoaches([new Coach("Kru Ana")]);

        var coach = Assert.Single(club.Coaches);
        Assert.Equal("Kru Ana", coach.Name);
        Assert.Null(coach.Contact);
    }

    [Fact]
    public void SetLogo_SetsUrl()
    {
        var club = new Club(OrgId, "Team Scorpion");

        club.SetLogo("https://media.local/clubs/x.webp");

        Assert.Equal("https://media.local/clubs/x.webp", club.LogoUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Coach_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Coach(name!));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Domain.Tests --filter ClubTests`
Expected: build error — `Club`/`Coach` do not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Sfc.Domain/Clubs/Coach.cs
namespace Sfc.Domain.Clubs;

/// <summary>
/// Simple value object — coaches are not users in Fase 1 (docs/02-modelo-dominio.md).
/// Persisted as part of the Club JSON column.
/// </summary>
public class Coach
{
    public string Name { get; private set; }
    public string? Contact { get; private set; }

    private Coach()
    {
        Name = null!;
    }

    public Coach(string name, string? contact = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Coach name is required.", nameof(name));

        Name = name.Trim();
        Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
    }
}
```

```csharp
// src/Sfc.Domain/Clubs/Club.cs
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Domain.Tests --filter ClubTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Domain/Clubs tests/Sfc.Domain.Tests/Clubs
git commit -m "Add Club entity with coaches value objects"
```

---

### Task 3: Athlete entity and enums (domain)

**Files:**
- Create: `src/Sfc.Domain/Athletes/Discipline.cs`
- Create: `src/Sfc.Domain/Athletes/AthleteStatus.cs`
- Create: `src/Sfc.Domain/Athletes/Athlete.cs`
- Test: `tests/Sfc.Domain.Tests/Athletes/AthleteTests.cs`

**Interfaces:**
- Consumes: `IOrganizationScoped`, `SlugGenerator.Generate` (Task 1), `Club` (Task 2, nav property only).
- Produces:
  - `enum Discipline { MuayThai, Kickboxing, K1, Boxing, Mma }`
  - `enum AthleteStatus { Amateur, Professional }`
  - `Athlete(Guid organizationId, string firstName, string lastName, DateOnly dateOfBirth, string nationality, Discipline discipline, AthleteStatus status, string slug, string? nickname = null, Guid? clubId = null, string? coachName = null, string? weightClass = null, decimal? weightKg = null, int? heightCm = null, bool publicProfileConsent = false, int baselineWins = 0, int baselineLosses = 0, int baselineDraws = 0, int baselineKos = 0)`
  - `void Athlete.Update(string firstName, string lastName, string? nickname, DateOnly dateOfBirth, string nationality, Discipline discipline, AthleteStatus status, Guid? clubId, string? coachName, string? weightClass, decimal? weightKg, int? heightCm, bool publicProfileConsent, bool isActive)` — never touches baseline.
  - `void Athlete.UpdateSlug(string slug)` — requires canonical slug format.
  - `void Athlete.SetPhoto(string photoUrl)`
  - Read-only record: `Wins`, `Losses`, `Draws`, `WinsByKo`, `RecordDisplay` ("W-L-D"), `Age`.
  - Nav property `Club? Club` (read-only, EF-populated).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Domain.Tests/Athletes/AthleteTests.cs
using Sfc.Domain.Athletes;
using Xunit;

namespace Sfc.Domain.Tests.Athletes;

public class AthleteTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Athlete CreateAthlete(
        int baselineWins = 0, int baselineLosses = 0, int baselineDraws = 0, int baselineKos = 0)
        => new(OrgId, "João", "Peixão", new DateOnly(2000, 5, 20), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao",
            baselineWins: baselineWins, baselineLosses: baselineLosses,
            baselineDraws: baselineDraws, baselineKos: baselineKos);

    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var athlete = CreateAthlete();

        Assert.NotEqual(Guid.Empty, athlete.Id);
        Assert.Equal(OrgId, athlete.OrganizationId);
        Assert.Equal("João", athlete.FirstName);
        Assert.Equal("Peixão", athlete.LastName);
        Assert.Equal("joao-peixao", athlete.Slug);
        Assert.True(athlete.IsActive);
        Assert.False(athlete.PublicProfileConsent);
    }

    [Fact]
    public void Constructor_InitialRecordEqualsBaseline()
    {
        var athlete = CreateAthlete(baselineWins: 18, baselineLosses: 3, baselineDraws: 1, baselineKos: 9);

        Assert.Equal(18, athlete.Wins);
        Assert.Equal(3, athlete.Losses);
        Assert.Equal(1, athlete.Draws);
        Assert.Equal(9, athlete.WinsByKo);
        Assert.Equal("18-3-1", athlete.RecordDisplay);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Constructor_WithNegativeBaseline_Throws(int wins, int losses, int draws, int kos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAthlete(baselineWins: wins, baselineLosses: losses, baselineDraws: draws, baselineKos: kos));
    }

    [Fact]
    public void Constructor_WithMoreKosThanWins_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateAthlete(baselineWins: 2, baselineKos: 3));
    }

    [Theory]
    [InlineData("", "Peixão")]
    [InlineData("João", "")]
    [InlineData("   ", "Peixão")]
    public void Constructor_WithBlankName_Throws(string firstName, string lastName)
    {
        Assert.Throws<ArgumentException>(() =>
            new Athlete(OrgId, firstName, lastName, new DateOnly(2000, 5, 20), "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, "slug"));
    }

    [Fact]
    public void Constructor_WithFutureDateOfBirth_Throws()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        Assert.Throws<ArgumentException>(() =>
            new Athlete(OrgId, "João", "Peixão", future, "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao"));
    }

    [Fact]
    public void Constructor_WithNonCanonicalSlug_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Athlete(OrgId, "João", "Peixão", new DateOnly(2000, 5, 20), "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, "João Peixão"));
    }

    [Fact]
    public void Update_NeverChangesBaseline()
    {
        var athlete = CreateAthlete(baselineWins: 10, baselineLosses: 2, baselineDraws: 0, baselineKos: 5);

        athlete.Update("Johnny", "Fish", "The Eel", new DateOnly(1999, 1, 1), "Brasil",
            Discipline.K1, AthleteStatus.Amateur, null, "Coach Zé", "-72kg", 71.5m, 180, true, false);

        Assert.Equal(10, athlete.Wins);
        Assert.Equal(2, athlete.Losses);
        Assert.Equal(5, athlete.WinsByKo);
        Assert.Equal("Johnny", athlete.FirstName);
        Assert.Equal("The Eel", athlete.Nickname);
        Assert.True(athlete.PublicProfileConsent);
        Assert.False(athlete.IsActive);
    }

    [Fact]
    public void UpdateSlug_WithCanonicalSlug_Changes()
    {
        var athlete = CreateAthlete();

        athlete.UpdateSlug("johnny-fish");

        Assert.Equal("johnny-fish", athlete.Slug);
    }

    [Fact]
    public void UpdateSlug_WithNonCanonicalSlug_Throws()
    {
        var athlete = CreateAthlete();

        Assert.Throws<ArgumentException>(() => athlete.UpdateSlug("Not A Slug"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5.5)]
    public void Update_WithNonPositiveWeight_Throws(double weight)
    {
        var athlete = CreateAthlete();

        Assert.Throws<ArgumentException>(() =>
            athlete.Update("João", "Peixão", null, new DateOnly(2000, 5, 20), "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, null, null, null,
                (decimal)weight, null, false, true));
    }

    [Fact]
    public void Age_IsComputedFromDateOfBirth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var birth = today.AddYears(-25);
        var athlete = new Athlete(OrgId, "João", "Peixão", birth, "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao");

        Assert.Equal(25, athlete.Age);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Domain.Tests --filter AthleteTests`
Expected: build error — `Athlete` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Sfc.Domain/Athletes/Discipline.cs
namespace Sfc.Domain.Athletes;

public enum Discipline
{
    MuayThai,
    Kickboxing,
    K1,
    Boxing,
    Mma,
}
```

```csharp
// src/Sfc.Domain/Athletes/AthleteStatus.cs
namespace Sfc.Domain.Athletes;

public enum AthleteStatus
{
    Amateur,
    Professional,
}
```

```csharp
// src/Sfc.Domain/Athletes/Athlete.cs
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
        bool publicProfileConsent = false,
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
            clubId, coachName, weightClass, weightKg, heightCm, publicProfileConsent);
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
        bool publicProfileConsent, bool isActive)
    {
        SetProfile(firstName, lastName, nickname, dateOfBirth, nationality, discipline, status,
            clubId, coachName, weightClass, weightKg, heightCm, publicProfileConsent);
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
        bool publicProfileConsent)
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
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Domain.Tests --filter AthleteTests`
Expected: all PASS.

- [ ] **Step 5: Run the whole domain suite and commit**

Run: `dotnet test tests/Sfc.Domain.Tests`
Expected: all PASS.

```bash
git add src/Sfc.Domain/Athletes tests/Sfc.Domain.Tests/Athletes
git commit -m "Add Athlete entity with baseline record invariants"
```

---

### Task 4: EF Core configuration and migration

**Files:**
- Modify: `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`
- Create: `src/Sfc.Infrastructure/Migrations/*_AddClubsAndAthletes.cs` (generated)
- Test: `tests/Sfc.Web.Tests/Persistence/ClubAthletePersistenceTests.cs`

**Interfaces:**
- Consumes: `Club`, `Coach` (Task 2), `Athlete` (Task 3).
- Produces: `DbSet<Club> SfcDbContext.Clubs`, `DbSet<Athlete> SfcDbContext.Athletes`; unique index `(OrganizationId, Slug)` on athletes; `Coaches` as JSON column; FK `Athlete.ClubId → Clubs` with `Restrict`.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/Sfc.Web.Tests/Persistence/ClubAthletePersistenceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Clubs;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests.Persistence;

public class ClubAthletePersistenceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Club_WithCoaches_RoundTrips()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var club = new Club(db.CurrentOrganizationId, "Team Scorpion", "Lisboa", "Portugal");
        club.SetCoaches([new Coach("Mestre Rui", "rui@scorpion.pt"), new Coach("Kru Ana")]);
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Clubs.SingleAsync(c => c.Id == club.Id);

        Assert.Equal("Team Scorpion", loaded.Name);
        Assert.Equal(2, loaded.Coaches.Count);
        Assert.Equal("Mestre Rui", loaded.Coaches[0].Name);
        Assert.Null(loaded.Coaches[1].Contact);
    }

    [Fact]
    public async Task Athlete_WithClub_RoundTripsAndLoadsNavigation()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var club = new Club(db.CurrentOrganizationId, "Fight Lab");
        db.Clubs.Add(club);
        var athlete = new Athlete(db.CurrentOrganizationId, "João", "Peixão",
            new DateOnly(2000, 5, 20), "Portugal", Discipline.MuayThai,
            AthleteStatus.Professional, "joao-peixao-persistence",
            clubId: club.Id, baselineWins: 5, baselineKos: 2);
        db.Athletes.Add(athlete);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Athletes.Include(a => a.Club).SingleAsync(a => a.Id == athlete.Id);

        Assert.Equal("5-0-0", loaded.RecordDisplay);
        Assert.Equal("Fight Lab", loaded.Club!.Name);
        Assert.Equal(Discipline.MuayThai, loaded.Discipline);
    }

    [Fact]
    public async Task Athlete_DuplicateSlugInSameOrganization_ThrowsOnSave()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        db.Athletes.Add(NewAthlete(db, "duplicate-slug-test"));
        await db.SaveChangesAsync();
        db.Athletes.Add(NewAthlete(db, "duplicate-slug-test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Athlete NewAthlete(SfcDbContext db, string slug)
        => new(db.CurrentOrganizationId, "Ana", "Silva", new DateOnly(1998, 3, 1), "Portugal",
            Discipline.K1, AthleteStatus.Amateur, slug);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sfc.Web.Tests --filter ClubAthletePersistenceTests`
Expected: build error — `SfcDbContext.Clubs` does not exist.

- [ ] **Step 3: Add DbSets and configuration to SfcDbContext**

In `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`, add usings and DbSets:

```csharp
using Sfc.Domain.Athletes;
using Sfc.Domain.Clubs;
```

```csharp
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Athlete> Athletes => Set<Athlete>();
```

Inside `OnModelCreating`, after the `Organization` block and **before** `ApplyOrganizationQueryFilters(builder);`:

```csharp
        builder.Entity<Club>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.LogoUrl).HasMaxLength(500);
            entity.Property(c => c.City).HasMaxLength(100);
            entity.Property(c => c.Country).HasMaxLength(100);
            entity.Property(c => c.ContactEmail).HasMaxLength(200);
            entity.Property(c => c.ContactPhone).HasMaxLength(50);
            entity.OwnsMany(c => c.Coaches, coaches => coaches.ToJson());
            entity.HasIndex(c => c.OrganizationId);
        });

        builder.Entity<Athlete>(entity =>
        {
            entity.Property(a => a.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.LastName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Nickname).HasMaxLength(100);
            entity.Property(a => a.Slug).HasMaxLength(200).IsRequired();
            entity.Property(a => a.PhotoUrl).HasMaxLength(500);
            entity.Property(a => a.Nationality).HasMaxLength(100).IsRequired();
            entity.Property(a => a.CoachName).HasMaxLength(200);
            entity.Property(a => a.WeightClass).HasMaxLength(50);
            entity.Property(a => a.WeightKg).HasPrecision(5, 2);
            entity.Property(a => a.Discipline).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(a => new { a.OrganizationId, a.Slug }).IsUnique();
            entity.HasOne(a => a.Club)
                .WithMany()
                .HasForeignKey(a => a.ClubId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 4: Generate the migration**

Run:
```bash
dotnet ef migrations add AddClubsAndAthletes -p src/Sfc.Infrastructure -s src/Sfc.Web
```
Expected: new files `*_AddClubsAndAthletes.cs` under `src/Sfc.Infrastructure/Migrations/`. Open the migration and verify it creates `Clubs` and `Athletes` tables, a `Coaches` jsonb column on `Clubs`, and the unique index `IX_Athletes_OrganizationId_Slug`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter ClubAthletePersistenceTests`
Expected: all PASS (migration applied by `DatabaseSeeder.MigrateAsync` on startup).

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Infrastructure tests/Sfc.Web.Tests/Persistence
git commit -m "Add Clubs and Athletes persistence with AddClubsAndAthletes migration"
```

---

### Task 5: ImageProcessor (WebP conversion + resize)

**Files:**
- Modify: `src/Sfc.Infrastructure/Sfc.Infrastructure.csproj` (add ImageSharp)
- Create: `src/Sfc.Infrastructure/Images/InvalidImageException.cs`
- Create: `src/Sfc.Infrastructure/Images/ImageProcessor.cs`
- Test: `tests/Sfc.Web.Tests/Images/ImageProcessorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `static Task<MemoryStream> ImageProcessor.ToWebpAsync(Stream input, int maxDimension, CancellationToken ct = default)` — returns a WebP stream positioned at 0, longest side ≤ `maxDimension`; throws `InvalidImageException` for non-image input. Used by services (Tasks 8/10) with `maxDimension` 400 (logos) and 800 (photos).

- [ ] **Step 1: Add the ImageSharp package**

In `src/Sfc.Infrastructure/Sfc.Infrastructure.csproj`, add to the `PackageReference` ItemGroup:

```xml
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.*" />
```

Run: `dotnet restore`
Expected: restore succeeds.

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Images/ImageProcessorTests.cs
using Sfc.Infrastructure.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Images;

public class ImageProcessorTests
{
    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task ToWebpAsync_ConvertsToWebp()
    {
        using var png = await CreatePngAsync(100, 50);

        using var result = await ImageProcessor.ToWebpAsync(png, 800);

        var format = await Image.DetectFormatAsync(result);
        Assert.Equal("Webp", format.Name, ignoreCase: true);
    }

    [Fact]
    public async Task ToWebpAsync_ResizesDownToMaxDimensionKeepingAspectRatio()
    {
        using var png = await CreatePngAsync(1600, 800);

        using var result = await ImageProcessor.ToWebpAsync(png, 800);

        using var image = await Image.LoadAsync(result);
        Assert.Equal(800, image.Width);
        Assert.Equal(400, image.Height);
    }

    [Fact]
    public async Task ToWebpAsync_DoesNotUpscaleSmallImages()
    {
        using var png = await CreatePngAsync(200, 100);

        using var result = await ImageProcessor.ToWebpAsync(png, 800);

        using var image = await Image.LoadAsync(result);
        Assert.Equal(200, image.Width);
    }

    [Fact]
    public async Task ToWebpAsync_WithNonImageContent_ThrowsInvalidImageException()
    {
        using var garbage = new MemoryStream("this is not an image"u8.ToArray());

        await Assert.ThrowsAsync<InvalidImageException>(() => ImageProcessor.ToWebpAsync(garbage, 800));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter ImageProcessorTests`
Expected: build error — `ImageProcessor` does not exist.

- [ ] **Step 4: Write the implementation**

```csharp
// src/Sfc.Infrastructure/Images/InvalidImageException.cs
namespace Sfc.Infrastructure.Images;

public class InvalidImageException(string message, Exception? inner = null)
    : Exception(message, inner);
```

```csharp
// src/Sfc.Infrastructure/Images/ImageProcessor.cs
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Sfc.Infrastructure.Images;

public static class ImageProcessor
{
    private const int WebpQuality = 80;

    /// <summary>
    /// Validates the stream is a real image, resizes so the longest side is at
    /// most <paramref name="maxDimension"/> (never upscales), and re-encodes as WebP.
    /// </summary>
    public static async Task<MemoryStream> ToWebpAsync(
        Stream input, int maxDimension, CancellationToken ct = default)
    {
        Image image;
        try
        {
            image = await Image.LoadAsync(input, ct);
        }
        catch (ImageFormatException ex)
        {
            throw new InvalidImageException("File is not a valid image.", ex);
        }

        using (image)
        {
            if (image.Width > maxDimension || image.Height > maxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxDimension, maxDimension),
                }));
            }

            var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder { Quality = WebpQuality }, ct);
            output.Position = 0;
            return output;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter ImageProcessorTests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Infrastructure tests/Sfc.Web.Tests/Images
git commit -m "Add WebP image processor"
```

---

### Task 6: IImageStorage — S3 implementation and test fake

**Files:**
- Modify: `src/Sfc.Infrastructure/Sfc.Infrastructure.csproj` (add AWSSDK.S3)
- Create: `src/Sfc.Infrastructure/Storage/IImageStorage.cs`
- Create: `src/Sfc.Infrastructure/Storage/StorageOptions.cs`
- Create: `src/Sfc.Infrastructure/Storage/S3ImageStorage.cs`
- Create: `tests/Sfc.Web.Tests/Fakes/FakeImageStorage.cs`
- Modify: `src/Sfc.Web/Program.cs` (DI registration)
- Modify: `src/Sfc.Web/appsettings.json` (dev MinIO defaults)
- Modify: `tests/Sfc.Web.Tests/SfcWebApplicationFactory.cs` (swap in fake)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `interface IImageStorage { Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken ct = default); Task DeleteAsync(string key, CancellationToken ct = default); }` — `SaveAsync` returns the public URL.
  - `FakeImageStorage` with `Dictionary<string, byte[]> Saved` — registered in the test factory; services tests (Tasks 8/10) resolve `IImageStorage` and cast to it.

- [ ] **Step 1: Add the AWS SDK package**

In `src/Sfc.Infrastructure/Sfc.Infrastructure.csproj`, add:

```xml
    <PackageReference Include="AWSSDK.S3" Version="3.7.*" />
```

Run: `dotnet restore`
Expected: restore succeeds.

- [ ] **Step 2: Write the interface, options, and S3 implementation**

(No isolated unit test for the S3 client itself — it is a thin adapter over the SDK, verified manually against MinIO at the end of Task 11. Integration tests use the fake.)

```csharp
// src/Sfc.Infrastructure/Storage/IImageStorage.cs
namespace Sfc.Infrastructure.Storage;

public interface IImageStorage
{
    /// <summary>Uploads the content and returns its public URL.</summary>
    Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

```csharp
// src/Sfc.Infrastructure/Storage/StorageOptions.cs
namespace Sfc.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; set; } = "";
    public string Bucket { get; set; } = "sfc-media";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    /// <summary>Base URL public clients use to reach objects (MinIO dev: endpoint + bucket).</summary>
    public string PublicBaseUrl { get; set; } = "";
}
```

```csharp
// src/Sfc.Infrastructure/Storage/S3ImageStorage.cs
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Sfc.Infrastructure.Storage;

/// <summary>
/// S3-compatible storage: MinIO in dev, Cloudflare R2 in production.
/// </summary>
public sealed class S3ImageStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IImageStorage
{
    private readonly StorageOptions _options = options.Value;
    private bool _bucketVerified;

    public async Task<string> SaveAsync(
        Stream content, string key, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(ct);

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
        }, ct);

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => s3.DeleteObjectAsync(_options.Bucket, key, ct);

    private async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        if (_bucketVerified)
            return;

        if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, _options.Bucket))
            await s3.PutBucketAsync(_options.Bucket, ct);

        _bucketVerified = true;
    }
}
```

- [ ] **Step 3: Register in DI and configure dev defaults**

In `src/Sfc.Web/Program.cs`, add usings:

```csharp
using Amazon.S3;
using Microsoft.Extensions.Options;
using Sfc.Infrastructure.Storage;
```

After the Identity registration block, add:

```csharp
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var storage = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    var config = new AmazonS3Config
    {
        ServiceURL = storage.Endpoint,
        ForcePathStyle = true, // required by MinIO
        AuthenticationRegion = "us-east-1",
    };
    return new AmazonS3Client(storage.AccessKey, storage.SecretKey, config);
});
builder.Services.AddSingleton<IImageStorage, S3ImageStorage>();
```

In `src/Sfc.Web/appsettings.json`, add a `Storage` section (local MinIO defaults, mirroring docker-compose — production overrides via environment variables, never committed):

```json
  "Storage": {
    "Endpoint": "http://localhost:9000",
    "Bucket": "sfc-media",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "PublicBaseUrl": "http://localhost:9000/sfc-media"
  }
```

- [ ] **Step 4: Create the fake and register it in the test factory**

```csharp
// tests/Sfc.Web.Tests/Fakes/FakeImageStorage.cs
using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Tests.Fakes;

public sealed class FakeImageStorage : IImageStorage
{
    public Dictionary<string, byte[]> Saved { get; } = [];

    public async Task<string> SaveAsync(
        Stream content, string key, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Saved[key] = buffer.ToArray();
        return $"https://media.test.local/{key}";
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Saved.Remove(key);
        return Task.CompletedTask;
    }
}
```

Replace `tests/Sfc.Web.Tests/SfcWebApplicationFactory.cs` with:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Infrastructure.Storage;
using Sfc.Web.Tests.Fakes;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sfc.Web.Tests;

public sealed class SfcWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public FakeImageStorage ImageStorage { get; } = new();

    public Task InitializeAsync() => _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("SeedAdmin:Email", "admin@test.local");
        builder.UseSetting("SeedAdmin:Password", "Test-Admin-2026!");
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IImageStorage>(ImageStorage));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

- [ ] **Step 5: Verify everything still builds and passes**

Run: `dotnet build && dotnet test tests/Sfc.Web.Tests`
Expected: build succeeds, all existing tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Infrastructure src/Sfc.Web tests/Sfc.Web.Tests
git commit -m "Add S3-compatible image storage with test fake"
```

---

### Task 7: Cookie authentication, login page, layout

**Files:**
- Modify: `src/Sfc.Web/Program.cs`
- Create: `src/Sfc.Web/Pages/_ViewStart.cshtml`
- Create: `src/Sfc.Web/Pages/Shared/_Layout.cshtml`
- Create: `src/Sfc.Web/Pages/Account/Login.cshtml` + `Login.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Account/Logout.cshtml` + `Logout.cshtml.cs`
- Create: `src/Sfc.Web/wwwroot/lib/bootstrap/bootstrap.min.css` + `bootstrap.bundle.min.js` (downloaded)
- Create: `tests/Sfc.Web.Tests/AuthTestHelper.cs`
- Test: `tests/Sfc.Web.Tests/Auth/AuthenticationTests.cs`

**Interfaces:**
- Consumes: seeded admin `admin@test.local` / `Test-Admin-2026!` (existing factory).
- Produces:
  - All pages under `/Admin` require authentication (`AuthorizeFolder("/Admin")`).
  - `static Task AuthTestHelper.LoginAsAdminAsync(HttpClient client)` — logs the client in via the real form (cookies preserved). Used by Tasks 9 and 11 tests.
  - `static Task<string> AuthTestHelper.GetAntiforgeryTokenAsync(HttpClient client, string getUrl)`.
  - Shared Bootstrap layout with pt-PT navigation (Atletas, Clubes, Sair).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/AuthTestHelper.cs
using System.Text.RegularExpressions;

namespace Sfc.Web.Tests;

public static partial class AuthTestHelper
{
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)"""")]
    private static partial Regex AntiforgeryTokenRegex();

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string getUrl)
    {
        var response = await client.GetAsync(getUrl);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex().Match(html);
        if (!match.Success)
            throw new InvalidOperationException($"No antiforgery token found in {getUrl}.");
        return match.Groups[1].Value;
    }

    public static async Task LoginAsAdminAsync(HttpClient client)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", "admin@test.local"),
            new KeyValuePair<string, string>("Input.Password", "Test-Admin-2026!"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]));

        if (response.StatusCode != System.Net.HttpStatusCode.Redirect &&
            !response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login failed: {response.StatusCode}");
        }
    }
}
```

```csharp
// tests/Sfc.Web.Tests/Auth/AuthenticationTests.cs
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Sfc.Web.Tests.Auth;

public class AuthenticationTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task AdminPage_WhenAnonymous_RedirectsToLogin()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/Admin/Athletes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WithSeededAdmin_GrantsAccessToAdminArea()
    {
        using var client = factory.CreateClient();

        await AuthTestHelper.LoginAsAdminAsync(client);
        var response = await client.GetAsync("/Admin/Athletes");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsError()
    {
        using var client = factory.CreateClient();
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", "admin@test.local"),
            new KeyValuePair<string, string>("Input.Password", "wrong"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Credenciais inválidas", html);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter AuthenticationTests`
Expected: FAIL — `/Admin/Athletes` returns 404 (page doesn't exist yet) instead of a redirect, and `/Account/Login` returns 404.

Note: `/Admin/Athletes/Index` only exists after Task 11. For the redirect test to be meaningful now, the `AuthorizeFolder` convention must apply before the 404 — ASP.NET evaluates authorization before endpoint match only for matched endpoints, so create a placeholder page in this task (Step 4) to make `/Admin/Athletes` real.

- [ ] **Step 3: Wire authentication in Program.cs**

Replace the service registrations in `src/Sfc.Web/Program.cs` so the full file becomes:

```csharp
using Amazon.S3;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;
using Sfc.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
    options.Conventions.AuthorizeFolder("/Admin"));
builder.Services.AddDbContext<SfcDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole>()
    .AddSignInManager()
    .AddEntityFrameworkStores<SfcDbContext>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});
builder.Services.AddAuthorization();

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var storage = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    var config = new AmazonS3Config
    {
        ServiceURL = storage.Endpoint,
        ForcePathStyle = true, // required by MinIO
        AuthenticationRegion = "us-east-1",
    };
    return new AmazonS3Client(storage.AccessKey, storage.SecretKey, config);
});
builder.Services.AddSingleton<IImageStorage, S3ImageStorage>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

await DatabaseSeeder.SeedAsync(app.Services, app.Configuration);

app.Run();

public partial class Program;
```

(Keep the Task 6 storage block if already present — this is the same code.)

- [ ] **Step 4: Download Bootstrap and create layout + login/logout pages + placeholder**

Download Bootstrap 5.3 locally (no CDN at runtime, no build step):

```bash
mkdir -p src/Sfc.Web/wwwroot/lib/bootstrap
curl -fsSL -o src/Sfc.Web/wwwroot/lib/bootstrap/bootstrap.min.css https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css
curl -fsSL -o src/Sfc.Web/wwwroot/lib/bootstrap/bootstrap.bundle.min.js https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js
```

```cshtml
@* src/Sfc.Web/Pages/_ViewStart.cshtml *@
@{
    Layout = "_Layout";
}
```

```cshtml
@* src/Sfc.Web/Pages/Shared/_Layout.cshtml *@
<!DOCTYPE html>
<html lang="pt">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>@ViewData["Title"] — SFC Backoffice</title>
    <link rel="stylesheet" href="~/lib/bootstrap/bootstrap.min.css" />
</head>
<body>
<nav class="navbar navbar-expand-lg navbar-dark bg-dark">
    <div class="container-fluid">
        <a class="navbar-brand" asp-page="/Index">SFC</a>
        <button class="navbar-toggler" type="button" data-bs-toggle="collapse"
                data-bs-target="#mainNav" aria-controls="mainNav" aria-expanded="false"
                aria-label="Alternar navegação">
            <span class="navbar-toggler-icon"></span>
        </button>
        <div class="collapse navbar-collapse" id="mainNav">
            <ul class="navbar-nav me-auto">
                <li class="nav-item"><a class="nav-link" asp-page="/Admin/Athletes/Index">Atletas</a></li>
                <li class="nav-item"><a class="nav-link" asp-page="/Admin/Clubs/Index">Clubes</a></li>
            </ul>
            @if (User.Identity?.IsAuthenticated == true)
            {
                <form method="post" asp-page="/Account/Logout" class="d-flex">
                    <button type="submit" class="btn btn-outline-light btn-sm">Sair</button>
                </form>
            }
        </div>
    </div>
</nav>
<main class="container py-4">
    @if (TempData["Success"] is string success)
    {
        <div class="alert alert-success alert-dismissible" role="alert">
            @success
            <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Fechar"></button>
        </div>
    }
    @RenderBody()
</main>
<script src="~/lib/bootstrap/bootstrap.bundle.min.js"></script>
</body>
</html>
```

```csharp
// src/Sfc.Web/Pages/Account/Login.cshtml.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sfc.Web.Pages.Account;

public class LoginModel(SignInManager<IdentityUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "O email é obrigatório.")]
        [EmailAddress(ErrorMessage = "Email inválido.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "A palavra-passe é obrigatória.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var target = returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/Admin/Athletes/Index";

        if (!ModelState.IsValid)
            return Page();

        var result = await signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, isPersistent: true, lockoutOnFailure: false);

        if (result.Succeeded)
            return LocalRedirect(target);

        ModelState.AddModelError(string.Empty, "Credenciais inválidas.");
        return Page();
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Account/Login.cshtml *@
@page
@model Sfc.Web.Pages.Account.LoginModel
@{
    ViewData["Title"] = "Entrar";
}
<div class="row justify-content-center">
    <div class="col-12 col-sm-8 col-md-5">
        <h1 class="h3 mb-4">Entrar</h1>
        <form method="post" asp-route-returnUrl="@Model.ReturnUrl">
            <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
            <div class="mb-3">
                <label asp-for="Input.Email" class="form-label">Email</label>
                <input asp-for="Input.Email" class="form-control" autocomplete="username" />
                <span asp-validation-for="Input.Email" class="text-danger"></span>
            </div>
            <div class="mb-3">
                <label asp-for="Input.Password" class="form-label">Palavra-passe</label>
                <input asp-for="Input.Password" class="form-control" autocomplete="current-password" />
                <span asp-validation-for="Input.Password" class="text-danger"></span>
            </div>
            <button type="submit" class="btn btn-primary w-100">Entrar</button>
        </form>
    </div>
</div>
```

```csharp
// src/Sfc.Web/Pages/Account/Logout.cshtml.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sfc.Web.Pages.Account;

public class LogoutModel(SignInManager<IdentityUser> signInManager) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Account/Logout.cshtml *@
@page
@model Sfc.Web.Pages.Account.LogoutModel
```

Placeholder athletes index (replaced by the real page in Task 11):

```cshtml
@* src/Sfc.Web/Pages/Admin/Athletes/Index.cshtml *@
@page
@{
    ViewData["Title"] = "Atletas";
}
<h1 class="h3">Atletas</h1>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter AuthenticationTests`
Expected: all PASS.

Run: `dotnet test tests/Sfc.Web.Tests`
Expected: all PASS (`HomePage_ReturnsSuccess` still green — `/` stays anonymous).

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests
git commit -m "Add cookie authentication with login page and Bootstrap layout"
```

---

### Task 8: ClubService

**Files:**
- Create: `src/Sfc.Web/Services/ClubService.cs`
- Test: `tests/Sfc.Web.Tests/Services/ClubServiceTests.cs`

**Interfaces:**
- Consumes: `SfcDbContext.Clubs/Athletes` (Task 4), `IImageStorage` (Task 6), `ImageProcessor` (Task 5), `Club`/`Coach` (Task 2).
- Produces (all used by Task 9 pages):
  - `record ClubInput(string Name, string? City, string? Country, string? ContactEmail, string? ContactPhone, string? CoachesText)`
  - `Task<List<Club>> ClubService.SearchAsync(string? name, CancellationToken ct = default)`
  - `Task<Club?> ClubService.GetAsync(Guid id, CancellationToken ct = default)`
  - `Task<Club> ClubService.CreateAsync(ClubInput input, Stream? logo, CancellationToken ct = default)`
  - `Task<Club?> ClubService.UpdateAsync(Guid id, ClubInput input, Stream? logo, CancellationToken ct = default)`
  - `enum ClubDeleteResult { Deleted, NotFound, HasAthletes }` + `Task<ClubDeleteResult> ClubService.DeleteAsync(Guid id, CancellationToken ct = default)`
  - `static List<Coach> ClubService.ParseCoaches(string? text)` — one coach per line, format `Name; contact` (contact optional).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Services/ClubServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class ClubServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static ClubInput Input(string name = "Team Scorpion") =>
        new(name, "Lisboa", "Portugal", null, null, "Mestre Rui; rui@scorpion.pt\nKru Ana");

    [Fact]
    public void ParseCoaches_ParsesOnePerLineWithOptionalContact()
    {
        var coaches = ClubService.ParseCoaches("Mestre Rui; rui@scorpion.pt\n\nKru Ana\n  ");

        Assert.Equal(2, coaches.Count);
        Assert.Equal("Mestre Rui", coaches[0].Name);
        Assert.Equal("rui@scorpion.pt", coaches[0].Contact);
        Assert.Equal("Kru Ana", coaches[1].Name);
        Assert.Null(coaches[1].Contact);
    }

    [Fact]
    public void ParseCoaches_WithNull_ReturnsEmpty()
    {
        Assert.Empty(ClubService.ParseCoaches(null));
    }

    [Fact]
    public async Task CreateAsync_PersistsClubWithCoaches()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();

        var club = await service.CreateAsync(Input(), logo: null);

        var loaded = await service.GetAsync(club.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Team Scorpion", loaded.Name);
        Assert.Equal(2, loaded.Coaches.Count);
    }

    [Fact]
    public async Task CreateAsync_WithLogo_ProcessesAndStoresWebp()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        using var png = await CreatePngAsync(600, 600);

        var club = await service.CreateAsync(Input("Logo Club"), png);

        Assert.Equal($"https://media.test.local/clubs/{club.Id}.webp", club.LogoUrl);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"clubs/{club.Id}.webp"));
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameCaseInsensitive()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        await service.CreateAsync(Input("Search Target Gym"), null);

        var results = await service.SearchAsync("search target");

        Assert.Contains(results, c => c.Name == "Search Target Gym");
    }

    [Fact]
    public async Task UpdateAsync_ChangesDetailsAndCoaches()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = await service.CreateAsync(Input("Before Update"), null);

        var updated = await service.UpdateAsync(club.Id,
            new ClubInput("After Update", "Porto", "Portugal", "x@y.pt", null, "Novo Treinador"), null);

        Assert.NotNull(updated);
        Assert.Equal("After Update", updated.Name);
        var coach = Assert.Single(updated.Coaches);
        Assert.Equal("Novo Treinador", coach.Name);
    }

    [Fact]
    public async Task DeleteAsync_WithAthletes_ReturnsHasAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var club = await service.CreateAsync(Input("Blocked Delete"), null);
        db.Athletes.Add(new Athlete(db.CurrentOrganizationId, "Ana", "Silva",
            new DateOnly(1998, 3, 1), "Portugal", Discipline.K1, AthleteStatus.Amateur,
            "ana-silva-blocked-delete", clubId: club.Id));
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(club.Id);

        Assert.Equal(ClubDeleteResult.HasAthletes, result);
        Assert.NotNull(await service.GetAsync(club.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithoutAthletes_Deletes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = await service.CreateAsync(Input("Free To Delete"), null);

        var result = await service.DeleteAsync(club.Id);

        Assert.Equal(ClubDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(club.Id));
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter ClubServiceTests`
Expected: build error — `ClubService` does not exist.

- [ ] **Step 3: Write the implementation and register in DI**

```csharp
// src/Sfc.Web/Services/ClubService.cs
using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Clubs;
using Sfc.Infrastructure.Images;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Services;

public record ClubInput(string Name, string? City, string? Country,
    string? ContactEmail, string? ContactPhone, string? CoachesText);

public enum ClubDeleteResult
{
    Deleted,
    NotFound,
    HasAthletes,
}

public class ClubService(SfcDbContext db, IImageStorage imageStorage)
{
    private const int LogoMaxDimension = 400;

    public async Task<List<Club>> SearchAsync(string? name, CancellationToken ct = default)
    {
        var query = db.Clubs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{name.Trim()}%"));

        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public Task<Club?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Clubs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Club> CreateAsync(ClubInput input, Stream? logo, CancellationToken ct = default)
    {
        var club = new Club(db.CurrentOrganizationId, input.Name, input.City, input.Country,
            input.ContactEmail, input.ContactPhone);
        club.SetCoaches(ParseCoaches(input.CoachesText));

        if (logo is not null)
            club.SetLogo(await UploadLogoAsync(club.Id, logo, ct));

        db.Clubs.Add(club);
        await db.SaveChangesAsync(ct);
        return club;
    }

    public async Task<Club?> UpdateAsync(Guid id, ClubInput input, Stream? logo,
        CancellationToken ct = default)
    {
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (club is null)
            return null;

        club.Update(input.Name, input.City, input.Country, input.ContactEmail, input.ContactPhone);
        club.SetCoaches(ParseCoaches(input.CoachesText));

        if (logo is not null)
            club.SetLogo(await UploadLogoAsync(club.Id, logo, ct));

        await db.SaveChangesAsync(ct);
        return club;
    }

    public async Task<ClubDeleteResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (club is null)
            return ClubDeleteResult.NotFound;

        if (await db.Athletes.AnyAsync(a => a.ClubId == id, ct))
            return ClubDeleteResult.HasAthletes;

        db.Clubs.Remove(club);
        await db.SaveChangesAsync(ct);

        if (club.LogoUrl is not null)
            await imageStorage.DeleteAsync($"clubs/{club.Id}.webp", ct);

        return ClubDeleteResult.Deleted;
    }

    /// <summary>One coach per line: "Name; contact" — contact optional.</summary>
    public static List<Coach> ParseCoaches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var parts = line.Split(';', 2, StringSplitOptions.TrimEntries);
                return new Coach(parts[0], parts.Length > 1 ? parts[1] : null);
            })
            .ToList();
    }

    private async Task<string> UploadLogoAsync(Guid clubId, Stream logo, CancellationToken ct)
    {
        using var webp = await ImageProcessor.ToWebpAsync(logo, LogoMaxDimension, ct);
        return await imageStorage.SaveAsync(webp, $"clubs/{clubId}.webp", "image/webp", ct);
    }
}
```

Register in `src/Sfc.Web/Program.cs` (after the storage registrations), adding the using `Sfc.Web.Services`:

```csharp
builder.Services.AddScoped<ClubService>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter ClubServiceTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Services
git commit -m "Add club service with coach parsing and logo upload"
```

---

### Task 9: Clubs Razor Pages (backoffice CRUD)

**Files:**
- Create: `src/Sfc.Web/Pages/Admin/Clubs/Index.cshtml` + `Index.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Clubs/Create.cshtml` + `Create.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Clubs/Edit.cshtml` + `Edit.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Clubs/Delete.cshtml` + `Delete.cshtml.cs`
- Test: `tests/Sfc.Web.Tests/Pages/ClubPagesTests.cs`

**Interfaces:**
- Consumes: `ClubService` + `ClubInput` + `ClubDeleteResult` (Task 8), `AuthTestHelper` (Task 7), `InvalidImageException` (Task 5).
- Produces: pages at `/Admin/Clubs`, `/Admin/Clubs/Create`, `/Admin/Clubs/Edit/{id}`, `/Admin/Clubs/Delete/{id}`. Shared form view-model class `ClubForm` (in `Create.cshtml.cs`, reused by Edit).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Pages/ClubPagesTests.cs
using Microsoft.Extensions.DependencyInjection;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class ClubPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Index_WhenAuthenticated_ListsClubs()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        await service.CreateAsync(new ClubInput("Clube Página Teste", "Lisboa", "Portugal",
            null, null, null), null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync("/Admin/Clubs");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Clube Página Teste", html);
    }

    [Theory]
    [InlineData("/Admin/Clubs/Create")]
    public async Task FormPages_WhenAuthenticated_Render(string url)
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_PostsFormAndRedirectsToIndex()
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Admin/Clubs/Create");

        var response = await client.PostAsync("/Admin/Clubs/Create", new MultipartFormDataContent
        {
            { new StringContent("Clube Criado Via Form"), "Form.Name" },
            { new StringContent("Lisboa"), "Form.City" },
            { new StringContent("Treinador A; 912"), "Form.CoachesText" },
            { new StringContent(token), "__RequestVerificationToken" },
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var results = await service.SearchAsync("Clube Criado Via Form");
        var club = Assert.Single(results);
        var coach = Assert.Single(club.Coaches);
        Assert.Equal("Treinador A", coach.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter ClubPagesTests`
Expected: FAIL — `/Admin/Clubs` returns 404.

- [ ] **Step 3: Create the pages**

```csharp
// src/Sfc.Web/Pages/Admin/Clubs/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Clubs;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class IndexModel(ClubService clubService) : PageModel
{
    public List<Club> Clubs { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
        => Clubs = await clubService.SearchAsync(Search, ct);
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Clubs/Index.cshtml *@
@page
@model Sfc.Web.Pages.Admin.Clubs.IndexModel
@{
    ViewData["Title"] = "Clubes";
}
<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h3 mb-0">Clubes</h1>
    <a asp-page="Create" class="btn btn-primary">Novo clube</a>
</div>
<form method="get" class="row g-2 mb-3">
    <div class="col-12 col-md-6">
        <input asp-for="Search" class="form-control" placeholder="Pesquisar por nome…" />
    </div>
    <div class="col-auto">
        <button type="submit" class="btn btn-outline-secondary">Pesquisar</button>
    </div>
</form>
@if (Model.Clubs.Count == 0)
{
    <p class="text-muted">Sem clubes registados.</p>
}
else
{
    <div class="table-responsive">
        <table class="table table-hover align-middle">
            <thead>
                <tr>
                    <th></th>
                    <th>Nome</th>
                    <th class="d-none d-md-table-cell">Cidade</th>
                    <th class="d-none d-md-table-cell">Treinadores</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
            @foreach (var club in Model.Clubs)
            {
                <tr>
                    <td style="width:56px">
                        @if (club.LogoUrl is not null)
                        {
                            <img src="@club.LogoUrl" alt="Logo de @club.Name" width="40" height="40"
                                 class="rounded object-fit-cover" />
                        }
                    </td>
                    <td>@club.Name</td>
                    <td class="d-none d-md-table-cell">@club.City</td>
                    <td class="d-none d-md-table-cell">@string.Join(", ", club.Coaches.Select(c => c.Name))</td>
                    <td class="text-end">
                        <a asp-page="Edit" asp-route-id="@club.Id" class="btn btn-sm btn-outline-secondary">Editar</a>
                        <a asp-page="Delete" asp-route-id="@club.Id" class="btn btn-sm btn-outline-danger">Apagar</a>
                    </td>
                </tr>
            }
            </tbody>
        </table>
    </div>
}
```

```csharp
// src/Sfc.Web/Pages/Admin/Clubs/Create.cshtml.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class ClubForm
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(200, ErrorMessage = "Máximo de 200 caracteres.")]
    public string Name { get; set; } = "";

    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; }

    [EmailAddress(ErrorMessage = "Email inválido.")]
    [StringLength(200)]
    public string? ContactEmail { get; set; }

    [StringLength(50)] public string? ContactPhone { get; set; }

    public string? CoachesText { get; set; }

    public ClubInput ToInput()
        => new(Name, City, Country, ContactEmail, ContactPhone, CoachesText);
}

public class CreateModel(ClubService clubService) : PageModel
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    [BindProperty]
    public ClubForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Logo { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Logo is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Logo", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await using var logoStream = Logo?.OpenReadStream();
            await clubService.CreateAsync(Form.ToInput(), logoStream, ct);
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Logo", "O ficheiro não é uma imagem válida.");
            return Page();
        }

        TempData["Success"] = "Clube criado com sucesso.";
        return RedirectToPage("Index");
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Clubs/Create.cshtml *@
@page
@model Sfc.Web.Pages.Admin.Clubs.CreateModel
@{
    ViewData["Title"] = "Novo clube";
}
<h1 class="h3 mb-4">Novo clube</h1>
<form method="post" enctype="multipart/form-data" class="col-12 col-md-8 col-lg-6">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
    <div class="mb-3">
        <label asp-for="Form.Name" class="form-label">Nome *</label>
        <input asp-for="Form.Name" class="form-control" />
        <span asp-validation-for="Form.Name" class="text-danger"></span>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.City" class="form-label">Cidade</label>
            <input asp-for="Form.City" class="form-control" />
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.Country" class="form-label">País</label>
            <input asp-for="Form.Country" class="form-control" />
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.ContactEmail" class="form-label">Email de contacto</label>
        <input asp-for="Form.ContactEmail" class="form-control" />
        <span asp-validation-for="Form.ContactEmail" class="text-danger"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Form.ContactPhone" class="form-label">Telefone de contacto</label>
        <input asp-for="Form.ContactPhone" class="form-control" />
    </div>
    <div class="mb-3">
        <label asp-for="Form.CoachesText" class="form-label">Treinadores</label>
        <textarea asp-for="Form.CoachesText" class="form-control" rows="3"
                  placeholder="Um por linha: Nome; contacto (opcional)"></textarea>
        <div class="form-text">Um treinador por linha, no formato "Nome; contacto". O contacto é opcional.</div>
    </div>
    <div class="mb-4">
        <label asp-for="Logo" class="form-label">Logo</label>
        <input asp-for="Logo" type="file" accept="image/*" class="form-control" />
        <span asp-validation-for="Logo" class="text-danger"></span>
    </div>
    <button type="submit" class="btn btn-primary">Guardar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

```csharp
// src/Sfc.Web/Pages/Admin/Clubs/Edit.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class EditModel(ClubService clubService) : PageModel
{
    [BindProperty]
    public ClubForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Logo { get; set; }

    public string? CurrentLogoUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var club = await clubService.GetAsync(id, ct);
        if (club is null)
            return NotFound();

        Form = new ClubForm
        {
            Name = club.Name,
            City = club.City,
            Country = club.Country,
            ContactEmail = club.ContactEmail,
            ContactPhone = club.ContactPhone,
            CoachesText = string.Join("\n",
                club.Coaches.Select(c => c.Contact is null ? c.Name : $"{c.Name}; {c.Contact}")),
        };
        CurrentLogoUrl = club.LogoUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (Logo is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Logo", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await using var logoStream = Logo?.OpenReadStream();
            var club = await clubService.UpdateAsync(id, Form.ToInput(), logoStream, ct);
            if (club is null)
                return NotFound();
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Logo", "O ficheiro não é uma imagem válida.");
            return Page();
        }

        TempData["Success"] = "Clube atualizado.";
        return RedirectToPage("Index");
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Clubs/Edit.cshtml *@
@page "{id:guid}"
@model Sfc.Web.Pages.Admin.Clubs.EditModel
@{
    ViewData["Title"] = "Editar clube";
}
<h1 class="h3 mb-4">Editar clube</h1>
<form method="post" enctype="multipart/form-data" class="col-12 col-md-8 col-lg-6">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
    <div class="mb-3">
        <label asp-for="Form.Name" class="form-label">Nome *</label>
        <input asp-for="Form.Name" class="form-control" />
        <span asp-validation-for="Form.Name" class="text-danger"></span>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.City" class="form-label">Cidade</label>
            <input asp-for="Form.City" class="form-control" />
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.Country" class="form-label">País</label>
            <input asp-for="Form.Country" class="form-control" />
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.ContactEmail" class="form-label">Email de contacto</label>
        <input asp-for="Form.ContactEmail" class="form-control" />
        <span asp-validation-for="Form.ContactEmail" class="text-danger"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Form.ContactPhone" class="form-label">Telefone de contacto</label>
        <input asp-for="Form.ContactPhone" class="form-control" />
    </div>
    <div class="mb-3">
        <label asp-for="Form.CoachesText" class="form-label">Treinadores</label>
        <textarea asp-for="Form.CoachesText" class="form-control" rows="3"
                  placeholder="Um por linha: Nome; contacto (opcional)"></textarea>
        <div class="form-text">Um treinador por linha, no formato "Nome; contacto". O contacto é opcional.</div>
    </div>
    <div class="mb-4">
        <label asp-for="Logo" class="form-label">Logo</label>
        @if (Model.CurrentLogoUrl is not null)
        {
            <div class="mb-2">
                <img src="@Model.CurrentLogoUrl" alt="Logo atual" width="80" height="80"
                     class="rounded object-fit-cover" />
            </div>
        }
        <input asp-for="Logo" type="file" accept="image/*" class="form-control" />
        <span asp-validation-for="Logo" class="text-danger"></span>
        <div class="form-text">Carregar nova imagem substitui a atual.</div>
    </div>
    <button type="submit" class="btn btn-primary">Guardar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

```csharp
// src/Sfc.Web/Pages/Admin/Clubs/Delete.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Clubs;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Clubs;

public class DeleteModel(ClubService clubService) : PageModel
{
    public Club? Club { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Club = await clubService.GetAsync(id, ct);
        return Club is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var result = await clubService.DeleteAsync(id, ct);
        switch (result)
        {
            case ClubDeleteResult.NotFound:
                return NotFound();
            case ClubDeleteResult.HasAthletes:
                Club = await clubService.GetAsync(id, ct);
                ModelState.AddModelError(string.Empty,
                    "Não é possível apagar um clube com atletas associados. Reatribua ou remova os atletas primeiro.");
                return Page();
            default:
                TempData["Success"] = "Clube apagado.";
                return RedirectToPage("Index");
        }
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Clubs/Delete.cshtml *@
@page "{id:guid}"
@model Sfc.Web.Pages.Admin.Clubs.DeleteModel
@{
    ViewData["Title"] = "Apagar clube";
}
<h1 class="h3 mb-4">Apagar clube</h1>
<div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
<p>Tem a certeza que quer apagar o clube <strong>@Model.Club?.Name</strong>? Esta ação não pode ser desfeita.</p>
<form method="post">
    <button type="submit" class="btn btn-danger">Apagar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter ClubPagesTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web/Pages/Admin/Clubs tests/Sfc.Web.Tests/Pages
git commit -m "Add clubs backoffice CRUD pages"
```

---

### Task 10: AthleteService (slug uniqueness, search, photo)

**Files:**
- Create: `src/Sfc.Web/Services/AthleteService.cs`
- Modify: `src/Sfc.Web/Program.cs` (register service)
- Test: `tests/Sfc.Web.Tests/Services/AthleteServiceTests.cs`

**Interfaces:**
- Consumes: `Athlete`/`Discipline`/`AthleteStatus` (Task 3), `SlugGenerator` (Task 1), `SfcDbContext` (Task 4), `IImageStorage` (Task 6), `ImageProcessor` (Task 5).
- Produces (used by Task 11 pages):
  - `record AthleteInput(string FirstName, string LastName, string? Nickname, DateOnly DateOfBirth, string Nationality, Discipline Discipline, AthleteStatus Status, Guid? ClubId, string? CoachName, string? WeightClass, decimal? WeightKg, int? HeightCm, bool PublicProfileConsent, string? Slug)`
  - `record AthleteListItem(Guid Id, string FullName, string? Nickname, string? PhotoUrl, string? ClubName, Discipline Discipline, string Record, bool IsActive)`
  - `record AthleteSearchResult(List<AthleteListItem> Items, int TotalCount, int Page, int PageSize)`
  - `Task<AthleteSearchResult> AthleteService.SearchAsync(string? name, Guid? clubId, Discipline? discipline, int page = 1, CancellationToken ct = default)` — `PageSize` 20.
  - `Task<Athlete?> AthleteService.GetAsync(Guid id, CancellationToken ct = default)`
  - `Task<Athlete> AthleteService.CreateAsync(AthleteInput input, (int Wins, int Losses, int Draws, int Kos) baseline, Stream? photo, CancellationToken ct = default)` — generates slug from name when `input.Slug` blank; always ensures uniqueness with `-2`, `-3`… suffix.
  - `Task<Athlete?> AthleteService.UpdateAsync(Guid id, AthleteInput input, bool isActive, Stream? photo, CancellationToken ct = default)`
  - `Task<bool> AthleteService.DeleteAsync(Guid id, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Services/AthleteServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class AthleteServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput Input(string firstName, string lastName,
        Guid? clubId = null, Discipline discipline = Discipline.MuayThai, string? slug = null)
        => new(firstName, lastName, null, new DateOnly(2000, 5, 20), "Portugal",
            discipline, AthleteStatus.Professional, clubId, null, null, null, null, false, slug);

    [Fact]
    public async Task CreateAsync_GeneratesSlugFromName()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();

        var athlete = await service.CreateAsync(Input("Zé", "Slugueiro"), (0, 0, 0, 0), null);

        Assert.Equal("ze-slugueiro", athlete.Slug);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_GetsNumericSuffix()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();

        await service.CreateAsync(Input("Dupla", "Colisão"), (0, 0, 0, 0), null);
        var second = await service.CreateAsync(Input("Dupla", "Colisão"), (0, 0, 0, 0), null);
        var third = await service.CreateAsync(Input("Dupla", "Colisão"), (0, 0, 0, 0), null);

        Assert.Equal("dupla-colisao-2", second.Slug);
        Assert.Equal("dupla-colisao-3", third.Slug);
    }

    [Fact]
    public async Task CreateAsync_WithBaseline_SetsInitialRecord()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();

        var athlete = await service.CreateAsync(Input("Com", "Cartel"), (18, 3, 1, 9), null);

        Assert.Equal("18-3-1", athlete.RecordDisplay);
        Assert.Equal(9, athlete.WinsByKo);
    }

    [Fact]
    public async Task CreateAsync_WithPhoto_StoresWebpAndSetsUrl()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        using var png = await CreatePngAsync(1000, 1000);

        var athlete = await service.CreateAsync(Input("Foto", "Grafado"), (0, 0, 0, 0), png);

        Assert.Equal($"https://media.test.local/athletes/{athlete.Id}.webp", athlete.PhotoUrl);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"athletes/{athlete.Id}.webp"));
    }

    [Fact]
    public async Task UpdateAsync_WithExplicitSlug_KeepsItUniqueExcludingSelf()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var athlete = await service.CreateAsync(Input("Slug", "Próprio"), (0, 0, 0, 0), null);

        // Re-saving with its own slug must not grow a suffix.
        var updated = await service.UpdateAsync(athlete.Id,
            Input("Slug", "Próprio", slug: "slug-proprio"), isActive: true, null);

        Assert.Equal("slug-proprio", updated!.Slug);
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameClubAndDiscipline()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var clubService = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = await clubService.CreateAsync(
            new ClubInput("Clube Filtro", null, null, null, null, null), null);
        await service.CreateAsync(Input("Filtrável", "Alvo", club.Id, Discipline.K1), (0, 0, 0, 0), null);
        await service.CreateAsync(Input("Outro", "Atleta", null, Discipline.Boxing), (0, 0, 0, 0), null);

        var byName = await service.SearchAsync("filtrável alvo", null, null);
        var byClub = await service.SearchAsync(null, club.Id, null);
        var byDiscipline = await service.SearchAsync("Filtrável", null, Discipline.K1);
        var noMatch = await service.SearchAsync("Filtrável", null, Discipline.Boxing);

        Assert.Contains(byName.Items, a => a.FullName == "Filtrável Alvo");
        Assert.Contains(byClub.Items, a => a.ClubName == "Clube Filtro");
        Assert.Contains(byDiscipline.Items, a => a.FullName == "Filtrável Alvo");
        Assert.DoesNotContain(noMatch.Items, a => a.FullName == "Filtrável Alvo");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAthlete()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var athlete = await service.CreateAsync(Input("Para", "Apagar"), (0, 0, 0, 0), null);

        var deleted = await service.DeleteAsync(athlete.Id);

        Assert.True(deleted);
        Assert.Null(await service.GetAsync(athlete.Id));
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter AthleteServiceTests`
Expected: build error — `AthleteService` does not exist.

- [ ] **Step 3: Write the implementation and register in DI**

```csharp
// src/Sfc.Web/Services/AthleteService.cs
using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Athletes;
using Sfc.Domain.Common;
using Sfc.Infrastructure.Images;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Services;

public record AthleteInput(string FirstName, string LastName, string? Nickname,
    DateOnly DateOfBirth, string Nationality, Discipline Discipline, AthleteStatus Status,
    Guid? ClubId, string? CoachName, string? WeightClass, decimal? WeightKg, int? HeightCm,
    bool PublicProfileConsent, string? Slug);

public record AthleteListItem(Guid Id, string FullName, string? Nickname, string? PhotoUrl,
    string? ClubName, Discipline Discipline, string Record, bool IsActive);

public record AthleteSearchResult(List<AthleteListItem> Items, int TotalCount, int Page, int PageSize);

public class AthleteService(SfcDbContext db, IImageStorage imageStorage)
{
    public const int PageSize = 20;
    private const int PhotoMaxDimension = 800;

    public async Task<AthleteSearchResult> SearchAsync(string? name, Guid? clubId,
        Discipline? discipline, int page = 1, CancellationToken ct = default)
    {
        var query = db.Athletes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var pattern = $"%{name.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.FirstName + " " + a.LastName, pattern) ||
                (a.Nickname != null && EF.Functions.ILike(a.Nickname, pattern)));
        }

        if (clubId is not null)
            query = query.Where(a => a.ClubId == clubId);

        if (discipline is not null)
            query = query.Where(a => a.Discipline == discipline);

        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);

        // Project raw values and build the record string in memory — int-to-string
        // concatenation is not reliably translatable to SQL.
        var rows = await query
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.Nickname,
                a.PhotoUrl,
                ClubName = a.Club != null ? a.Club.Name : null,
                a.Discipline,
                a.BaselineWins,
                a.BaselineLosses,
                a.BaselineDraws,
                a.IsActive,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new AthleteListItem(r.Id, $"{r.FirstName} {r.LastName}", r.Nickname,
                r.PhotoUrl, r.ClubName, r.Discipline,
                $"{r.BaselineWins}-{r.BaselineLosses}-{r.BaselineDraws}", r.IsActive))
            .ToList();

        return new AthleteSearchResult(items, total, page, PageSize);
    }

    public Task<Athlete?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Athletes.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Athlete> CreateAsync(AthleteInput input,
        (int Wins, int Losses, int Draws, int Kos) baseline, Stream? photo,
        CancellationToken ct = default)
    {
        var slug = await ResolveSlugAsync(input, excludeId: null, ct);

        var athlete = new Athlete(db.CurrentOrganizationId, input.FirstName, input.LastName,
            input.DateOfBirth, input.Nationality, input.Discipline, input.Status, slug,
            input.Nickname, input.ClubId, input.CoachName, input.WeightClass, input.WeightKg,
            input.HeightCm, input.PublicProfileConsent,
            baseline.Wins, baseline.Losses, baseline.Draws, baseline.Kos);

        if (photo is not null)
            athlete.SetPhoto(await UploadPhotoAsync(athlete.Id, photo, ct));

        db.Athletes.Add(athlete);
        await db.SaveChangesAsync(ct);
        return athlete;
    }

    public async Task<Athlete?> UpdateAsync(Guid id, AthleteInput input, bool isActive,
        Stream? photo, CancellationToken ct = default)
    {
        var athlete = await db.Athletes.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (athlete is null)
            return null;

        athlete.Update(input.FirstName, input.LastName, input.Nickname, input.DateOfBirth,
            input.Nationality, input.Discipline, input.Status, input.ClubId, input.CoachName,
            input.WeightClass, input.WeightKg, input.HeightCm, input.PublicProfileConsent,
            isActive);

        var slug = await ResolveSlugAsync(input, excludeId: id, ct);
        if (slug != athlete.Slug)
            athlete.UpdateSlug(slug);

        if (photo is not null)
            athlete.SetPhoto(await UploadPhotoAsync(athlete.Id, photo, ct));

        await db.SaveChangesAsync(ct);
        return athlete;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var athlete = await db.Athletes.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (athlete is null)
            return false;

        db.Athletes.Remove(athlete);
        await db.SaveChangesAsync(ct);

        if (athlete.PhotoUrl is not null)
            await imageStorage.DeleteAsync($"athletes/{athlete.Id}.webp", ct);

        return true;
    }

    private async Task<string> ResolveSlugAsync(AthleteInput input, Guid? excludeId,
        CancellationToken ct)
    {
        var baseSlug = SlugGenerator.Generate(
            string.IsNullOrWhiteSpace(input.Slug)
                ? $"{input.FirstName} {input.LastName}"
                : input.Slug);

        var slug = baseSlug;
        var suffix = 2;
        while (await db.Athletes.AnyAsync(
            a => a.Slug == slug && (excludeId == null || a.Id != excludeId), ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private async Task<string> UploadPhotoAsync(Guid athleteId, Stream photo, CancellationToken ct)
    {
        using var webp = await ImageProcessor.ToWebpAsync(photo, PhotoMaxDimension, ct);
        return await imageStorage.SaveAsync(webp, $"athletes/{athleteId}.webp", "image/webp", ct);
    }
}
```

In `src/Sfc.Web/Program.cs`, next to `AddScoped<ClubService>()`:

```csharp
builder.Services.AddScoped<AthleteService>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter AthleteServiceTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Services
git commit -m "Add athlete service with slug uniqueness and photo upload"
```

---

### Task 11: Athletes Razor Pages (backoffice CRUD + search)

**Files:**
- Create: `src/Sfc.Web/Services/PtDisplay.cs`
- Modify: `src/Sfc.Web/Pages/Admin/Athletes/Index.cshtml` (replace Task 7 placeholder) + create `Index.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Athletes/Create.cshtml` + `Create.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Athletes/Edit.cshtml` + `Edit.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Athletes/Delete.cshtml` + `Delete.cshtml.cs`
- Test: `tests/Sfc.Web.Tests/Pages/AthletePagesTests.cs`

**Interfaces:**
- Consumes: `AthleteService` + records (Task 10), `ClubService` (Task 8), `AuthTestHelper` (Task 7), `InvalidImageException` (Task 5).
- Produces: pages at `/Admin/Athletes`, `/Admin/Athletes/Create`, `/Admin/Athletes/Edit/{id}`, `/Admin/Athletes/Delete/{id}`; `static string PtDisplay.ToDisplay(this Discipline)` and `ToDisplay(this AthleteStatus)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Pages/AthletePagesTests.cs
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class AthletePagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Index_ListsAthletesWithRecordAndSupportsNameFilter()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        await service.CreateAsync(new AthleteInput("Página", "Listada", "A Lenda",
            new DateOnly(1995, 1, 1), "Portugal", Discipline.MuayThai, AthleteStatus.Professional,
            null, null, null, null, null, false, null), (12, 2, 0, 6), null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var listed = await client.GetAsync("/Admin/Athletes");
        var listedHtml = await listed.Content.ReadAsStringAsync();
        var filtered = await client.GetAsync("/Admin/Athletes?Search=página listada");
        var filteredHtml = await filtered.Content.ReadAsStringAsync();
        var noMatch = await client.GetAsync("/Admin/Athletes?Search=inexistente-xyz");
        var noMatchHtml = await noMatch.Content.ReadAsStringAsync();

        Assert.Contains("Página Listada", listedHtml);
        Assert.Contains("12-2-0", listedHtml);
        Assert.Contains("Página Listada", filteredHtml);
        Assert.DoesNotContain("Página Listada", noMatchHtml);
    }

    [Fact]
    public async Task Create_PostsFormWithBaselineAndRedirects()
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Admin/Athletes/Create");

        var response = await client.PostAsync("/Admin/Athletes/Create", new MultipartFormDataContent
        {
            { new StringContent("Criado"), "Form.FirstName" },
            { new StringContent("Via Form"), "Form.LastName" },
            { new StringContent("1999-04-15"), "Form.DateOfBirth" },
            { new StringContent("Portugal"), "Form.Nationality" },
            { new StringContent("MuayThai"), "Form.Discipline" },
            { new StringContent("Amateur"), "Form.Status" },
            { new StringContent("3"), "Form.BaselineWins" },
            { new StringContent("1"), "Form.BaselineLosses" },
            { new StringContent("0"), "Form.BaselineDraws" },
            { new StringContent("2"), "Form.BaselineKos" },
            { new StringContent("false"), "Form.PublicProfileConsent" },
            { new StringContent(token), "__RequestVerificationToken" },
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var results = await service.SearchAsync("Criado Via Form", null, null);
        var item = Assert.Single(results.Items);
        Assert.Equal("3-1-0", item.Record);
    }

    [Theory]
    [InlineData("/Admin/Athletes/Create")]
    public async Task FormPages_WhenAuthenticated_Render(string url)
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter AthletePagesTests`
Expected: FAIL — placeholder index has no data; `/Admin/Athletes/Create` returns 404.

- [ ] **Step 3: Create the display helper and pages**

```csharp
// src/Sfc.Web/Services/PtDisplay.cs
using Sfc.Domain.Athletes;

namespace Sfc.Web.Services;

/// <summary>UI display names in pt-PT (code stays in English — CLAUDE.md rule 4).</summary>
public static class PtDisplay
{
    public static string ToDisplay(this Discipline discipline) => discipline switch
    {
        Discipline.MuayThai => "Muay Thai",
        Discipline.Kickboxing => "Kickboxing",
        Discipline.K1 => "K1",
        Discipline.Boxing => "Boxe",
        Discipline.Mma => "MMA",
        _ => discipline.ToString(),
    };

    public static string ToDisplay(this AthleteStatus status) => status switch
    {
        AthleteStatus.Amateur => "Amador",
        AthleteStatus.Professional => "Profissional",
        _ => status.ToString(),
    };
}
```

```csharp
// src/Sfc.Web/Pages/Admin/Athletes/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class IndexModel(AthleteService athleteService, ClubService clubService) : PageModel
{
    public AthleteSearchResult Result { get; private set; } =
        new([], 0, 1, AthleteService.PageSize);

    public List<SelectListItem> ClubOptions { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ClubId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Discipline? Discipline { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Result = await athleteService.SearchAsync(Search, ClubId, Discipline, P, ct);
        ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Athletes/Index.cshtml *@
@page
@using Sfc.Domain.Athletes
@using Sfc.Web.Services
@model Sfc.Web.Pages.Admin.Athletes.IndexModel
@{
    ViewData["Title"] = "Atletas";
    var totalPages = (int)Math.Ceiling((double)Model.Result.TotalCount / Model.Result.PageSize);
}
<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h3 mb-0">Atletas</h1>
    <a asp-page="Create" class="btn btn-primary">Novo atleta</a>
</div>
<form method="get" class="row g-2 mb-3">
    <div class="col-12 col-md-4">
        <input asp-for="Search" class="form-control" placeholder="Pesquisar por nome ou alcunha…" />
    </div>
    <div class="col-6 col-md-3">
        <select asp-for="ClubId" asp-items="Model.ClubOptions" class="form-select">
            <option value="">Todos os clubes</option>
        </select>
    </div>
    <div class="col-6 col-md-3">
        <select asp-for="Discipline" class="form-select">
            <option value="">Todas as disciplinas</option>
            @foreach (var d in Enum.GetValues<Discipline>())
            {
                <option value="@d">@d.ToDisplay()</option>
            }
        </select>
    </div>
    <div class="col-auto">
        <button type="submit" class="btn btn-outline-secondary">Filtrar</button>
    </div>
</form>
@if (Model.Result.Items.Count == 0)
{
    <p class="text-muted">Sem atletas para os filtros escolhidos.</p>
}
else
{
    <div class="table-responsive">
        <table class="table table-hover align-middle">
            <thead>
                <tr>
                    <th></th>
                    <th>Nome</th>
                    <th class="d-none d-md-table-cell">Clube</th>
                    <th class="d-none d-md-table-cell">Disciplina</th>
                    <th>Cartel</th>
                    <th class="d-none d-md-table-cell">Estado</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
            @foreach (var athlete in Model.Result.Items)
            {
                <tr>
                    <td style="width:56px">
                        @if (athlete.PhotoUrl is not null)
                        {
                            <img src="@athlete.PhotoUrl" alt="Foto de @athlete.FullName"
                                 width="40" height="40" class="rounded-circle object-fit-cover" />
                        }
                    </td>
                    <td>
                        @athlete.FullName
                        @if (athlete.Nickname is not null)
                        {
                            <span class="text-muted">"@athlete.Nickname"</span>
                        }
                    </td>
                    <td class="d-none d-md-table-cell">@athlete.ClubName</td>
                    <td class="d-none d-md-table-cell">@athlete.Discipline.ToDisplay()</td>
                    <td>@athlete.Record</td>
                    <td class="d-none d-md-table-cell">
                        @if (athlete.IsActive)
                        {
                            <span class="badge text-bg-success">Ativo</span>
                        }
                        else
                        {
                            <span class="badge text-bg-secondary">Inativo</span>
                        }
                    </td>
                    <td class="text-end">
                        <a asp-page="Edit" asp-route-id="@athlete.Id" class="btn btn-sm btn-outline-secondary">Editar</a>
                        <a asp-page="Delete" asp-route-id="@athlete.Id" class="btn btn-sm btn-outline-danger">Apagar</a>
                    </td>
                </tr>
            }
            </tbody>
        </table>
    </div>
    @if (totalPages > 1)
    {
        <nav aria-label="Paginação">
            <ul class="pagination">
            @for (var i = 1; i <= totalPages; i++)
            {
                <li class="page-item @(i == Model.Result.Page ? "active" : "")">
                    <a class="page-link" asp-page="Index" asp-route-p="@i"
                       asp-route-search="@Model.Search" asp-route-clubId="@Model.ClubId"
                       asp-route-discipline="@Model.Discipline">@i</a>
                </li>
            }
            </ul>
        </nav>
    }
}
```

```csharp
// src/Sfc.Web/Pages/Admin/Athletes/Create.cshtml.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class AthleteForm
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(100)]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "O apelido é obrigatório.")]
    [StringLength(100)]
    public string LastName { get; set; } = "";

    [StringLength(100)] public string? Nickname { get; set; }

    [Required(ErrorMessage = "A data de nascimento é obrigatória.")]
    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "A nacionalidade é obrigatória.")]
    [StringLength(100)]
    public string Nationality { get; set; } = "Portugal";

    [Required(ErrorMessage = "A disciplina é obrigatória.")]
    public Discipline? Discipline { get; set; }

    [Required(ErrorMessage = "O estatuto é obrigatório.")]
    public AthleteStatus? Status { get; set; }

    public Guid? ClubId { get; set; }
    [StringLength(200)] public string? CoachName { get; set; }
    [StringLength(50)] public string? WeightClass { get; set; }

    [Range(20, 200, ErrorMessage = "Peso entre 20 e 200 kg.")]
    public decimal? WeightKg { get; set; }

    [Range(100, 230, ErrorMessage = "Altura entre 100 e 230 cm.")]
    public int? HeightCm { get; set; }

    public bool PublicProfileConsent { get; set; }

    [StringLength(200)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug inválido: usar apenas minúsculas, números e hífens.")]
    public string? Slug { get; set; }

    public AthleteInput ToInput()
        => new(FirstName, LastName, Nickname, DateOfBirth!.Value, Nationality,
            Discipline!.Value, Status!.Value, ClubId, CoachName, WeightClass,
            WeightKg, HeightCm, PublicProfileConsent, Slug);
}

public class CreateModel(AthleteService athleteService, ClubService clubService) : PageModel
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    [BindProperty]
    public AthleteForm Form { get; set; } = new();

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineWins { get; set; }

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineLosses { get; set; }

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineDraws { get; set; }

    [BindProperty]
    [Range(0, 500, ErrorMessage = "Valor entre 0 e 500.")]
    public int BaselineKos { get; set; }

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public List<SelectListItem> ClubOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => await LoadClubsAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Photo is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Photo", "A imagem não pode exceder 10 MB.");
        if (BaselineKos > BaselineWins)
            ModelState.AddModelError("BaselineKos", "Os KOs não podem exceder as vitórias.");

        if (!ModelState.IsValid)
        {
            await LoadClubsAsync(ct);
            return Page();
        }

        try
        {
            await using var photoStream = Photo?.OpenReadStream();
            await athleteService.CreateAsync(Form.ToInput(),
                (BaselineWins, BaselineLosses, BaselineDraws, BaselineKos), photoStream, ct);
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Photo", "O ficheiro não é uma imagem válida.");
            await LoadClubsAsync(ct);
            return Page();
        }

        TempData["Success"] = "Atleta criado com sucesso.";
        return RedirectToPage("Index");
    }

    private async Task LoadClubsAsync(CancellationToken ct)
        => ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Athletes/Create.cshtml *@
@page
@using Sfc.Domain.Athletes
@using Sfc.Web.Services
@model Sfc.Web.Pages.Admin.Athletes.CreateModel
@{
    ViewData["Title"] = "Novo atleta";
}
<h1 class="h3 mb-4">Novo atleta</h1>
<form method="post" enctype="multipart/form-data" class="col-12 col-lg-8">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>

    <h2 class="h5">Identificação</h2>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.FirstName" class="form-label">Nome *</label>
            <input asp-for="Form.FirstName" class="form-control" />
            <span asp-validation-for="Form.FirstName" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.LastName" class="form-label">Apelido *</label>
            <input asp-for="Form.LastName" class="form-control" />
            <span asp-validation-for="Form.LastName" class="text-danger"></span>
        </div>
    </div>
    <div class="row">
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Nickname" class="form-label">Alcunha</label>
            <input asp-for="Form.Nickname" class="form-control" />
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.DateOfBirth" class="form-label">Data de nascimento *</label>
            <input asp-for="Form.DateOfBirth" type="date" class="form-control" />
            <span asp-validation-for="Form.DateOfBirth" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Nationality" class="form-label">Nacionalidade *</label>
            <input asp-for="Form.Nationality" class="form-control" />
            <span asp-validation-for="Form.Nationality" class="text-danger"></span>
        </div>
    </div>

    <h2 class="h5 mt-2">Desportivo</h2>
    <div class="row">
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Discipline" class="form-label">Disciplina *</label>
            <select asp-for="Form.Discipline" class="form-select">
                <option value="">Escolher…</option>
                @foreach (var d in Enum.GetValues<Discipline>())
                {
                    <option value="@d">@d.ToDisplay()</option>
                }
            </select>
            <span asp-validation-for="Form.Discipline" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Status" class="form-label">Estatuto *</label>
            <select asp-for="Form.Status" class="form-select">
                <option value="">Escolher…</option>
                @foreach (var s in Enum.GetValues<AthleteStatus>())
                {
                    <option value="@s">@s.ToDisplay()</option>
                }
            </select>
            <span asp-validation-for="Form.Status" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.ClubId" class="form-label">Clube</label>
            <select asp-for="Form.ClubId" asp-items="Model.ClubOptions" class="form-select">
                <option value="">Sem clube</option>
            </select>
        </div>
    </div>
    <div class="row">
        <div class="col-md-4 mb-3">
            <label asp-for="Form.CoachName" class="form-label">Treinador</label>
            <input asp-for="Form.CoachName" class="form-control" />
        </div>
        <div class="col-md-3 mb-3">
            <label asp-for="Form.WeightClass" class="form-label">Categoria de peso</label>
            <input asp-for="Form.WeightClass" class="form-control" placeholder="ex.: -72kg" />
        </div>
        <div class="col-md-2 mb-3">
            <label asp-for="Form.WeightKg" class="form-label">Peso (kg)</label>
            <input asp-for="Form.WeightKg" type="number" step="0.1" class="form-control" />
            <span asp-validation-for="Form.WeightKg" class="text-danger"></span>
        </div>
        <div class="col-md-3 mb-3">
            <label asp-for="Form.HeightCm" class="form-label">Altura (cm)</label>
            <input asp-for="Form.HeightCm" type="number" class="form-control" />
            <span asp-validation-for="Form.HeightCm" class="text-danger"></span>
        </div>
    </div>

    <h2 class="h5 mt-2">Cartel inicial (histórico antes da plataforma)</h2>
    <p class="form-text">Depois de criado, o cartel só muda com resultados de combates — não volta a ser editável.</p>
    <div class="row">
        <div class="col-3 mb-3">
            <label asp-for="BaselineWins" class="form-label">Vitórias</label>
            <input asp-for="BaselineWins" type="number" class="form-control" />
            <span asp-validation-for="BaselineWins" class="text-danger"></span>
        </div>
        <div class="col-3 mb-3">
            <label asp-for="BaselineLosses" class="form-label">Derrotas</label>
            <input asp-for="BaselineLosses" type="number" class="form-control" />
            <span asp-validation-for="BaselineLosses" class="text-danger"></span>
        </div>
        <div class="col-3 mb-3">
            <label asp-for="BaselineDraws" class="form-label">Empates</label>
            <input asp-for="BaselineDraws" type="number" class="form-control" />
            <span asp-validation-for="BaselineDraws" class="text-danger"></span>
        </div>
        <div class="col-3 mb-3">
            <label asp-for="BaselineKos" class="form-label">KOs</label>
            <input asp-for="BaselineKos" type="number" class="form-control" />
            <span asp-validation-for="BaselineKos" class="text-danger"></span>
        </div>
    </div>

    <h2 class="h5 mt-2">Publicação</h2>
    <div class="form-check mb-3">
        <input asp-for="Form.PublicProfileConsent" class="form-check-input" />
        <label asp-for="Form.PublicProfileConsent" class="form-check-label">
            Consentimento para perfil público (RGPD)
        </label>
        <div class="form-text">
            Sem consentimento, o atleta aparece no portal apenas com o nome no fight card.
            Menores exigem consentimento do encarregado de educação.
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.Slug" class="form-label">Slug (URL pública)</label>
        <input asp-for="Form.Slug" class="form-control" placeholder="gerado automaticamente do nome" />
        <span asp-validation-for="Form.Slug" class="text-danger"></span>
    </div>
    <div class="mb-4">
        <label asp-for="Photo" class="form-label">Foto</label>
        <input asp-for="Photo" type="file" accept="image/*" class="form-control" />
        <span asp-validation-for="Photo" class="text-danger"></span>
    </div>

    <button type="submit" class="btn btn-primary">Guardar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

```csharp
// src/Sfc.Web/Pages/Admin/Athletes/Edit.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class EditModel(AthleteService athleteService, ClubService clubService) : PageModel
{
    [BindProperty]
    public AthleteForm Form { get; set; } = new();

    [BindProperty]
    public bool IsActive { get; set; } = true;

    [BindProperty]
    public IFormFile? Photo { get; set; }

    public string? CurrentPhotoUrl { get; private set; }
    public string RecordDisplay { get; private set; } = "";
    public List<SelectListItem> ClubOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var athlete = await athleteService.GetAsync(id, ct);
        if (athlete is null)
            return NotFound();

        Form = new AthleteForm
        {
            FirstName = athlete.FirstName,
            LastName = athlete.LastName,
            Nickname = athlete.Nickname,
            DateOfBirth = athlete.DateOfBirth,
            Nationality = athlete.Nationality,
            Discipline = athlete.Discipline,
            Status = athlete.Status,
            ClubId = athlete.ClubId,
            CoachName = athlete.CoachName,
            WeightClass = athlete.WeightClass,
            WeightKg = athlete.WeightKg,
            HeightCm = athlete.HeightCm,
            PublicProfileConsent = athlete.PublicProfileConsent,
            Slug = athlete.Slug,
        };
        IsActive = athlete.IsActive;
        CurrentPhotoUrl = athlete.PhotoUrl;
        RecordDisplay = athlete.RecordDisplay;
        await LoadClubsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (Photo is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Photo", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
        {
            await LoadClubsAsync(ct);
            return Page();
        }

        try
        {
            await using var photoStream = Photo?.OpenReadStream();
            var athlete = await athleteService.UpdateAsync(id, Form.ToInput(), IsActive, photoStream, ct);
            if (athlete is null)
                return NotFound();
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Photo", "O ficheiro não é uma imagem válida.");
            await LoadClubsAsync(ct);
            return Page();
        }

        TempData["Success"] = "Atleta atualizado.";
        return RedirectToPage("Index");
    }

    private async Task LoadClubsAsync(CancellationToken ct)
        => ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Athletes/Edit.cshtml *@
@page "{id:guid}"
@using Sfc.Domain.Athletes
@using Sfc.Web.Services
@model Sfc.Web.Pages.Admin.Athletes.EditModel
@{
    ViewData["Title"] = "Editar atleta";
}
<h1 class="h3 mb-4">Editar atleta</h1>
<form method="post" enctype="multipart/form-data" class="col-12 col-lg-8">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>

    <div class="alert alert-secondary">
        Cartel atual: <strong>@Model.RecordDisplay</strong> — o cartel só muda com resultados de combates.
    </div>

    <h2 class="h5">Identificação</h2>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.FirstName" class="form-label">Nome *</label>
            <input asp-for="Form.FirstName" class="form-control" />
            <span asp-validation-for="Form.FirstName" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.LastName" class="form-label">Apelido *</label>
            <input asp-for="Form.LastName" class="form-control" />
            <span asp-validation-for="Form.LastName" class="text-danger"></span>
        </div>
    </div>
    <div class="row">
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Nickname" class="form-label">Alcunha</label>
            <input asp-for="Form.Nickname" class="form-control" />
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.DateOfBirth" class="form-label">Data de nascimento *</label>
            <input asp-for="Form.DateOfBirth" type="date" class="form-control" />
            <span asp-validation-for="Form.DateOfBirth" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Nationality" class="form-label">Nacionalidade *</label>
            <input asp-for="Form.Nationality" class="form-control" />
            <span asp-validation-for="Form.Nationality" class="text-danger"></span>
        </div>
    </div>

    <h2 class="h5 mt-2">Desportivo</h2>
    <div class="row">
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Discipline" class="form-label">Disciplina *</label>
            <select asp-for="Form.Discipline" class="form-select">
                <option value="">Escolher…</option>
                @foreach (var d in Enum.GetValues<Discipline>())
                {
                    <option value="@d">@d.ToDisplay()</option>
                }
            </select>
            <span asp-validation-for="Form.Discipline" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Status" class="form-label">Estatuto *</label>
            <select asp-for="Form.Status" class="form-select">
                <option value="">Escolher…</option>
                @foreach (var s in Enum.GetValues<AthleteStatus>())
                {
                    <option value="@s">@s.ToDisplay()</option>
                }
            </select>
            <span asp-validation-for="Form.Status" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.ClubId" class="form-label">Clube</label>
            <select asp-for="Form.ClubId" asp-items="Model.ClubOptions" class="form-select">
                <option value="">Sem clube</option>
            </select>
        </div>
    </div>
    <div class="row">
        <div class="col-md-4 mb-3">
            <label asp-for="Form.CoachName" class="form-label">Treinador</label>
            <input asp-for="Form.CoachName" class="form-control" />
        </div>
        <div class="col-md-3 mb-3">
            <label asp-for="Form.WeightClass" class="form-label">Categoria de peso</label>
            <input asp-for="Form.WeightClass" class="form-control" placeholder="ex.: -72kg" />
        </div>
        <div class="col-md-2 mb-3">
            <label asp-for="Form.WeightKg" class="form-label">Peso (kg)</label>
            <input asp-for="Form.WeightKg" type="number" step="0.1" class="form-control" />
            <span asp-validation-for="Form.WeightKg" class="text-danger"></span>
        </div>
        <div class="col-md-3 mb-3">
            <label asp-for="Form.HeightCm" class="form-label">Altura (cm)</label>
            <input asp-for="Form.HeightCm" type="number" class="form-control" />
            <span asp-validation-for="Form.HeightCm" class="text-danger"></span>
        </div>
    </div>

    <h2 class="h5 mt-2">Publicação e estado</h2>
    <div class="form-check mb-3">
        <input asp-for="Form.PublicProfileConsent" class="form-check-input" />
        <label asp-for="Form.PublicProfileConsent" class="form-check-label">
            Consentimento para perfil público (RGPD)
        </label>
    </div>
    <div class="form-check mb-3">
        <input asp-for="IsActive" class="form-check-input" />
        <label asp-for="IsActive" class="form-check-label">Ativo</label>
    </div>
    <div class="mb-3">
        <label asp-for="Form.Slug" class="form-label">Slug (URL pública)</label>
        <input asp-for="Form.Slug" class="form-control" />
        <span asp-validation-for="Form.Slug" class="text-danger"></span>
    </div>
    <div class="mb-4">
        <label asp-for="Photo" class="form-label">Foto</label>
        @if (Model.CurrentPhotoUrl is not null)
        {
            <div class="mb-2">
                <img src="@Model.CurrentPhotoUrl" alt="Foto atual" width="80" height="80"
                     class="rounded-circle object-fit-cover" />
            </div>
        }
        <input asp-for="Photo" type="file" accept="image/*" class="form-control" />
        <span asp-validation-for="Photo" class="text-danger"></span>
        <div class="form-text">Carregar nova imagem substitui a atual.</div>
    </div>

    <button type="submit" class="btn btn-primary">Guardar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

```csharp
// src/Sfc.Web/Pages/Admin/Athletes/Delete.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Athletes;

public class DeleteModel(AthleteService athleteService) : PageModel
{
    public Athlete? Athlete { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Athlete = await athleteService.GetAsync(id, ct);
        return Athlete is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (!await athleteService.DeleteAsync(id, ct))
            return NotFound();

        TempData["Success"] = "Atleta apagado.";
        return RedirectToPage("Index");
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Athletes/Delete.cshtml *@
@page "{id:guid}"
@model Sfc.Web.Pages.Admin.Athletes.DeleteModel
@{
    ViewData["Title"] = "Apagar atleta";
}
<h1 class="h3 mb-4">Apagar atleta</h1>
<p>
    Tem a certeza que quer apagar
    <strong>@Model.Athlete?.FirstName @Model.Athlete?.LastName</strong>?
    Esta ação não pode ser desfeita. Para afastamentos temporários, use antes o estado "Inativo" na edição.
</p>
<form method="post">
    <button type="submit" class="btn btn-danger">Apagar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter AthletePagesTests`
Expected: all PASS.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test`
Expected: all projects PASS.

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests
git commit -m "Add athletes backoffice CRUD pages with search and pagination"
```

---

### Task 12: Final verification and quality gates

**Files:**
- No new code — verification, agents, and PR preparation.

- [ ] **Step 1: Full build and test run**

Run: `dotnet build && dotnet test`
Expected: build with zero warnings (TreatWarningsAsErrors), all tests PASS.

- [ ] **Step 2: Manual smoke test against real MinIO (verifies the real S3 adapter)**

```bash
docker compose up -d
dotnet run --project src/Sfc.Web
```

With `SeedAdmin:Email`/`SeedAdmin:Password` set via user-secrets. In the browser: log in, create a club with a logo, create an athlete with a photo, confirm the images render from `http://localhost:9000/sfc-media/...`, test search filters on mobile viewport (devtools). Confirm slug collision behaviour by creating two athletes with the same name.

Note: MinIO buckets are private by default — if images do not render, run
`docker compose exec minio mc anonymous set download local/sfc-media` or set the bucket policy via the MinIO console at `http://localhost:9001`. Document whichever step was needed in `README.md`.

- [ ] **Step 3: Run the scope guardian agent**

Dispatch the `guardiao-ambito` agent over the branch diff (`git diff master...HEAD`) to validate nothing exceeds Fase 1 scope. Fix any findings.

- [ ] **Step 4: Run the domain reviewer agent**

Dispatch the `revisor-dominio` agent over the domain entities and UI copy (record semantics, disciplines, pt-PT vocabulary: cartel, alcunha, categoria de peso). Fix any findings.

- [ ] **Step 5: Security review**

Run the `/security-review` skill on the branch. Expected focus: upload handling (content validation via ImageSharp, size cap), no DoB exposure outside backoffice, no secrets in code, antiforgery on all POSTs (Razor Pages default).

- [ ] **Step 6: Push branch and open PR**

```bash
git push -u origin feature/atletas-clubes
gh pr create --title "Add clubs and athletes backoffice management" --body "$(cat <<'EOF'
## Summary
- Club and Athlete domain entities with baseline record invariants and slug generation
- Backoffice CRUD (Razor Pages) with search, photo/logo upload (WebP via ImageSharp, S3-compatible storage)
- Minimal cookie authentication protecting the /Admin area
- Design: docs/plans/2026-07-14-atletas-clubes-design.md

## Test plan
- [x] Domain unit tests (slug, baseline record, validations)
- [x] Integration tests (Testcontainers PostgreSQL): CRUD, slug uniqueness, auth redirects
- [x] Manual smoke test against MinIO

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR opens with CI (build + tests + gitleaks) green before merge.
