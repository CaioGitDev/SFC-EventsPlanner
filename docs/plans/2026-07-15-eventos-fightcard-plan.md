# Events and Fight Card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Backoffice CRUD for Events with banner/poster upload plus fight-card management (add/remove/reorder fights, athlete pick with search, athlete replacement), per `docs/plans/2026-07-15-eventos-fightcard-design.md`.

**Architecture:** `Event` is the aggregate root of its `Fight`s — all card operations and invariants (unique athlete per event, contiguous order, position-derived billing) are domain methods on `Event`, unit-tested without mocks. `EventService` orchestrates DbContext + image storage like the existing `ClubService`/`AthleteService`; Razor Pages stay thin.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, SixLabors.ImageSharp (existing `ImageProcessor`), existing `IImageStorage` (MinIO/R2), xUnit + Testcontainers PostgreSQL.

## Global Constraints

- Code, entities, tests in **English**; all user-visible strings in **pt-PT** (CLAUDE.md rule 4).
- Every entity implements `IOrganizationScoped`; the existing query-filter convention picks it up (ADR-002).
- Billing is NEVER edited manually: last Order = `Main`, second-to-last = `CoMain` (only when ≥2 fights), rest `Card`; recalculated on every card change.
- Event slug: unique per `(OrganizationId, Slug)`, generated from name, editable only while `PublishedAt == null`, immutable after first publication.
- `Event.Date` is a naive local `DateTime` stored as `timestamp without time zone` (single-country app; portal interprets Europe/Lisbon).
- Fight weight: `WeightClass` (string) XOR `CatchweightKg` (decimal) — exactly one.
- `ReplaceAthlete` only on `FightStatus.Scheduled` fights.
- Fights are created/mutated ONLY through `Event` aggregate methods (`Fight` ctor and mutators are `internal`).
- No repositories, no MediatR (ADR-001). `TreatWarningsAsErrors` is on.
- Never push to `master`; work on `feature/eventos-fightcard` (already created, based on master with PR #3 merged).
- Migrations are generated with `-p src/Sfc.Infrastructure -s src/Sfc.Infrastructure` (design-time factory).
- Working directory for all commands: repo root `D:\Users\160173003\Desktop\SFC-EventsPlanner`.

---

### Task 1: Event entity, EventStatus, state transitions, slug immutability (domain)

**Files:**
- Create: `src/Sfc.Domain/Events/EventStatus.cs`
- Create: `src/Sfc.Domain/Events/Event.cs` (card methods arrive in Task 3 — this task creates the entity WITHOUT fight methods but WITH the `_fights` list and `Fights` property, so Task 3 only adds methods)
- Test: `tests/Sfc.Domain.Tests/Events/EventTests.cs`

**Interfaces:**
- Consumes: `IOrganizationScoped`, `SlugGenerator` (existing).
- Produces:
  - `enum EventStatus { Draft, Published, Completed, Cancelled }`
  - `Event(Guid organizationId, string name, DateTime date, string slug, string? description = null, string? venue = null, string? city = null, string? ticketsUrl = null, string? streamUrl = null)`
  - `void Update(string name, string? description, DateTime date, string? venue, string? city, string? ticketsUrl, string? streamUrl)`
  - `void UpdateSlug(string slug)` — throws `InvalidOperationException` when `PublishedAt != null`
  - `void Publish()` / `void Unpublish()` / `void Complete()` / `void Cancel()` — invalid transitions throw `InvalidOperationException`
  - `void SetBanner(string url)` / `void SetPoster(string url)`
  - `DateTime? PublishedAt`, `IReadOnlyList<Fight> Fights` (empty until Task 3; `Fight` type arrives in Task 2 — see Step 3 note)

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Domain.Tests/Events/EventTests.cs
using Sfc.Domain.Events;
using Xunit;

namespace Sfc.Domain.Tests.Events;

public class EventTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Event CreateEvent(string name = "SFC 12", string slug = "sfc-12")
        => new(OrgId, name, new DateTime(2026, 9, 12, 20, 0, 0), slug,
            description: "Gala anual", venue: "Pavilhão Municipal", city: "Lisboa");

    [Fact]
    public void Constructor_WithValidData_SetsPropertiesAndDefaults()
    {
        var evt = CreateEvent();

        Assert.NotEqual(Guid.Empty, evt.Id);
        Assert.Equal(OrgId, evt.OrganizationId);
        Assert.Equal("SFC 12", evt.Name);
        Assert.Equal("sfc-12", evt.Slug);
        Assert.Equal(EventStatus.Draft, evt.Status);
        Assert.Null(evt.PublishedAt);
        Assert.Empty(evt.Fights);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            new Event(OrgId, name!, new DateTime(2026, 9, 12), "slug"));
    }

    [Fact]
    public void Constructor_WithDefaultDate_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Event(OrgId, "SFC 12", default, "sfc-12"));
    }

    [Fact]
    public void Constructor_WithNonCanonicalSlug_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Event(OrgId, "SFC 12", new DateTime(2026, 9, 12), "SFC 12"));
    }

    [Fact]
    public void Publish_FromDraft_SetsPublishedAtOnce()
    {
        var evt = CreateEvent();

        evt.Publish();
        var firstPublishedAt = evt.PublishedAt;
        evt.Unpublish();
        evt.Publish();

        Assert.Equal(EventStatus.Published, evt.Status);
        Assert.Equal(firstPublishedAt, evt.PublishedAt);
    }

    [Fact]
    public void Publish_WhenAlreadyPublished_Throws()
    {
        var evt = CreateEvent();
        evt.Publish();

        Assert.Throws<InvalidOperationException>(evt.Publish);
    }

    [Fact]
    public void Unpublish_FromDraft_Throws()
    {
        Assert.Throws<InvalidOperationException>(CreateEvent().Unpublish);
    }

    [Fact]
    public void Complete_FromPublished_Succeeds()
    {
        var evt = CreateEvent();
        evt.Publish();

        evt.Complete();

        Assert.Equal(EventStatus.Completed, evt.Status);
    }

    [Fact]
    public void Complete_FromDraft_Throws()
    {
        Assert.Throws<InvalidOperationException>(CreateEvent().Complete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_FromDraftOrPublished_Succeeds(bool publishFirst)
    {
        var evt = CreateEvent();
        if (publishFirst)
            evt.Publish();

        evt.Cancel();

        Assert.Equal(EventStatus.Cancelled, evt.Status);
    }

    [Fact]
    public void Cancel_FromCompleted_Throws()
    {
        var evt = CreateEvent();
        evt.Publish();
        evt.Complete();

        Assert.Throws<InvalidOperationException>(evt.Cancel);
    }

    [Fact]
    public void UpdateSlug_BeforeFirstPublication_Changes()
    {
        var evt = CreateEvent();

        evt.UpdateSlug("sfc-12-lisboa");

        Assert.Equal("sfc-12-lisboa", evt.Slug);
    }

    [Fact]
    public void UpdateSlug_AfterFirstPublication_ThrowsEvenIfUnpublished()
    {
        var evt = CreateEvent();
        evt.Publish();
        evt.Unpublish();

        Assert.Throws<InvalidOperationException>(() => evt.UpdateSlug("outro-slug"));
    }

    [Fact]
    public void Update_ChangesEditableFieldsOnly()
    {
        var evt = CreateEvent();

        evt.Update("SFC 12 — Noite de Campeões", "Nova descrição", new DateTime(2026, 9, 13, 21, 0, 0),
            "Altice Arena", "Lisboa", "https://tickets.example/sfc12", "https://youtube.com/watch?v=x");

        Assert.Equal("SFC 12 — Noite de Campeões", evt.Name);
        Assert.Equal(new DateTime(2026, 9, 13, 21, 0, 0), evt.Date);
        Assert.Equal("https://tickets.example/sfc12", evt.TicketsUrl);
        Assert.Equal("sfc-12", evt.Slug);
        Assert.Equal(EventStatus.Draft, evt.Status);
    }

    [Fact]
    public void SetBannerAndPoster_SetUrls()
    {
        var evt = CreateEvent();

        evt.SetBanner("https://media.local/events/x-banner.webp");
        evt.SetPoster("https://media.local/events/x-poster.webp");

        Assert.Equal("https://media.local/events/x-banner.webp", evt.BannerUrl);
        Assert.Equal("https://media.local/events/x-poster.webp", evt.PosterUrl);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Domain.Tests --filter EventTests`
Expected: build error — `Event` does not exist.

- [ ] **Step 3: Write the implementation**

Note: `Fights` references the `Fight` type from Task 2. To keep this task compilable on its own, create `src/Sfc.Domain/Events/Fight.cs` in Task 2 BEFORE this file compiles — or, if executing strictly in order, declare the `_fights` list and `Fights` property in this task exactly as shown (Task 2 delivers the type; run Task 1's tests after adding the minimal `Fight` from Task 2 if the build requires it). Recommended execution: implement Tasks 1 and 2 as written; Task 1's Step 4 test run happens after both files exist if the build demands the type. If you prefer strict isolation, omit the two `Fights` lines in this step and let Task 3 add them — either path is acceptable; the tests in this task do not exercise `Fights` beyond `Assert.Empty`.

```csharp
// src/Sfc.Domain/Events/EventStatus.cs
namespace Sfc.Domain.Events;

public enum EventStatus
{
    Draft,
    Published,
    Completed,
    Cancelled,
}
```

```csharp
// src/Sfc.Domain/Events/Event.cs
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Domain.Tests --filter EventTests`
Expected: all PASS (if the build needs the `Fight` type first, complete Task 2 Step 3 and re-run).

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Domain/Events tests/Sfc.Domain.Tests/Events
git commit -m "Add Event entity with status transitions and slug immutability"
```

---

### Task 2: Fight entity and enums (domain)

**Files:**
- Create: `src/Sfc.Domain/Events/FightBilling.cs`
- Create: `src/Sfc.Domain/Events/FightStatus.cs`
- Create: `src/Sfc.Domain/Events/Corner.cs`
- Create: `src/Sfc.Domain/Events/MoveDirection.cs`
- Create: `src/Sfc.Domain/Events/Fight.cs`
- Test: fight construction rules are tested through `Event.AddFight` in Task 3 (the `Fight` ctor is `internal`); this task only needs the solution to build.

**Interfaces:**
- Consumes: `Athlete`, `Discipline` (existing), `IOrganizationScoped`.
- Produces:
  - `enum FightBilling { Main, CoMain, Card }`
  - `enum FightStatus { Scheduled, Completed, Cancelled, NoContest }`
  - `enum Corner { Red, Blue }`
  - `enum MoveDirection { Up, Down }` — `Up` moves to a LOWER `Order` (earlier in the night); `Down` to a higher one (towards the main event)
  - `Fight` with public read-only properties (`Id`, `OrganizationId`, `EventId`, `Order`, `Billing`, `Discipline`, `Rounds`, `RoundDurationMinutes`, `WeightClass`, `CatchweightKg`, `IsTitleFight`, `IsAmateur`, `RedCornerAthleteId`, `RedCornerAthlete`, `BlueCornerAthleteId`, `BlueCornerAthlete`, `Status`, `CreatedAt`, `UpdatedAt`) and `internal` ctor + `internal SetOrder/SetBilling/ReplaceCorner` — only the `Event` aggregate mutates fights.

- [ ] **Step 1: Write the implementation**

```csharp
// src/Sfc.Domain/Events/FightBilling.cs
namespace Sfc.Domain.Events;

public enum FightBilling
{
    Main,
    CoMain,
    Card,
}
```

```csharp
// src/Sfc.Domain/Events/FightStatus.cs
namespace Sfc.Domain.Events;

/// <summary>Completed/Cancelled/NoContest only gain behavior with results (prompt 03).</summary>
public enum FightStatus
{
    Scheduled,
    Completed,
    Cancelled,
    NoContest,
}
```

```csharp
// src/Sfc.Domain/Events/Corner.cs
namespace Sfc.Domain.Events;

public enum Corner
{
    Red,
    Blue,
}
```

```csharp
// src/Sfc.Domain/Events/MoveDirection.cs
namespace Sfc.Domain.Events;

/// <summary>Up = lower Order (earlier in the night); Down = higher Order (towards the main event).</summary>
public enum MoveDirection
{
    Up,
    Down,
}
```

```csharp
// src/Sfc.Domain/Events/Fight.cs
using Sfc.Domain.Athletes;
using Sfc.Domain.Common;

namespace Sfc.Domain.Events;

/// <summary>
/// Created and mutated only through the <see cref="Event"/> aggregate, which owns
/// order contiguity, billing derivation, and athlete uniqueness in the card.
/// </summary>
public class Fight : IOrganizationScoped
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>1 opens the event; the highest order is the main event.</summary>
    public int Order { get; private set; }

    /// <summary>Derived from position by the aggregate — never set manually.</summary>
    public FightBilling Billing { get; private set; }

    public Discipline Discipline { get; private set; }
    public int Rounds { get; private set; }
    public int RoundDurationMinutes { get; private set; }
    public string? WeightClass { get; private set; }
    public decimal? CatchweightKg { get; private set; }
    public bool IsTitleFight { get; private set; }
    public bool IsAmateur { get; private set; }
    public Guid RedCornerAthleteId { get; private set; }
    public Athlete? RedCornerAthlete { get; private set; }
    public Guid BlueCornerAthleteId { get; private set; }
    public Athlete? BlueCornerAthlete { get; private set; }
    public FightStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Fight()
    {
    }

    internal Fight(Guid organizationId, Guid eventId, int order,
        Guid redCornerAthleteId, Guid blueCornerAthleteId, Discipline discipline,
        int rounds, int roundDurationMinutes, string? weightClass, decimal? catchweightKg,
        bool isTitleFight, bool isAmateur)
    {
        if (redCornerAthleteId == Guid.Empty)
            throw new ArgumentException("Red corner athlete is required.", nameof(redCornerAthleteId));
        if (blueCornerAthleteId == Guid.Empty)
            throw new ArgumentException("Blue corner athlete is required.", nameof(blueCornerAthleteId));
        if (redCornerAthleteId == blueCornerAthleteId)
            throw new ArgumentException("An athlete cannot be in both corners.", nameof(blueCornerAthleteId));
        if (rounds is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(rounds));
        if (roundDurationMinutes is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(roundDurationMinutes));
        if (catchweightKg is <= 0)
            throw new ArgumentException("Catchweight must be positive.", nameof(catchweightKg));

        var hasWeightClass = !string.IsNullOrWhiteSpace(weightClass);
        if (hasWeightClass == catchweightKg.HasValue)
            throw new ArgumentException(
                "Exactly one of weight class or catchweight is required.", nameof(weightClass));

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        EventId = eventId;
        Order = order;
        Billing = FightBilling.Card;
        Discipline = discipline;
        Rounds = rounds;
        RoundDurationMinutes = roundDurationMinutes;
        WeightClass = hasWeightClass ? weightClass!.Trim() : null;
        CatchweightKg = catchweightKg;
        IsTitleFight = isTitleFight;
        IsAmateur = isAmateur;
        RedCornerAthleteId = redCornerAthleteId;
        BlueCornerAthleteId = blueCornerAthleteId;
        Status = FightStatus.Scheduled;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    internal void SetOrder(int order)
    {
        Order = order;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void SetBilling(FightBilling billing)
    {
        Billing = billing;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void ReplaceCorner(Corner corner, Guid athleteId)
    {
        if (Status != FightStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled fights can have athletes replaced.");
        if (athleteId == Guid.Empty)
            throw new ArgumentException("Athlete is required.", nameof(athleteId));
        if (corner == Corner.Red)
        {
            RedCornerAthleteId = athleteId;
            RedCornerAthlete = null;
        }
        else
        {
            BlueCornerAthleteId = athleteId;
            BlueCornerAthlete = null;
        }

        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 2: Build and run the Task 1 tests**

Run: `dotnet build && dotnet test tests/Sfc.Domain.Tests --filter EventTests`
Expected: build clean (0 warnings), EventTests all PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Sfc.Domain/Events
git commit -m "Add Fight entity with aggregate-only mutators"
```

---

### Task 3: Fight card operations on the Event aggregate (domain)

**Files:**
- Modify: `src/Sfc.Domain/Events/Event.cs` (add card methods at the end of the class, before the private helpers)
- Test: `tests/Sfc.Domain.Tests/Events/FightCardTests.cs`

**Interfaces:**
- Consumes: `Fight` internal ctor/mutators (Task 2).
- Produces (used by `EventService` in Task 6):
  - `Fight Event.AddFight(Guid redCornerAthleteId, Guid blueCornerAthleteId, Discipline discipline, int rounds, int roundDurationMinutes, string? weightClass, decimal? catchweightKg, bool isTitleFight, bool isAmateur)`
  - `bool Event.RemoveFight(Guid fightId)` — false when not found; closes order gaps
  - `bool Event.MoveFight(Guid fightId, MoveDirection direction)` — false when already at the edge; throws `InvalidOperationException` when the fight is not in this event
  - `void Event.ReplaceAthlete(Guid fightId, Corner corner, Guid newAthleteId)`
  - `bool Event.HasAthlete(Guid athleteId)` — public so the service can pre-check for friendly errors

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Domain.Tests/Events/FightCardTests.cs
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Xunit;

namespace Sfc.Domain.Tests.Events;

public class FightCardTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Event CreateEvent()
        => new(OrgId, "SFC 12", new DateTime(2026, 9, 12, 20, 0, 0), "sfc-12");

    private static Fight AddFight(Event evt, Guid? red = null, Guid? blue = null)
        => evt.AddFight(red ?? Guid.NewGuid(), blue ?? Guid.NewGuid(), Discipline.MuayThai,
            rounds: 3, roundDurationMinutes: 3, weightClass: "-72kg", catchweightKg: null,
            isTitleFight: false, isAmateur: false);

    [Fact]
    public void AddFight_AppendsWithContiguousOrder()
    {
        var evt = CreateEvent();

        var first = AddFight(evt);
        var second = AddFight(evt);
        var third = AddFight(evt);

        Assert.Equal(1, first.Order);
        Assert.Equal(2, second.Order);
        Assert.Equal(3, third.Order);
        Assert.Equal(3, evt.Fights.Count);
    }

    [Fact]
    public void AddFight_DerivesBilling_LastIsMainSecondToLastIsCoMain()
    {
        var evt = CreateEvent();

        var f1 = AddFight(evt);
        Assert.Equal(FightBilling.Main, f1.Billing); // single fight is the main event

        var f2 = AddFight(evt);
        Assert.Equal(FightBilling.CoMain, f1.Billing);
        Assert.Equal(FightBilling.Main, f2.Billing);

        var f3 = AddFight(evt);
        Assert.Equal(FightBilling.Card, f1.Billing);
        Assert.Equal(FightBilling.CoMain, f2.Billing);
        Assert.Equal(FightBilling.Main, f3.Billing);
    }

    [Fact]
    public void AddFight_WithSameAthleteBothCorners_Throws()
    {
        var evt = CreateEvent();
        var athlete = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => AddFight(evt, red: athlete, blue: athlete));
    }

    [Fact]
    public void AddFight_WithAthleteAlreadyInCard_Throws()
    {
        var evt = CreateEvent();
        var repeated = Guid.NewGuid();
        AddFight(evt, red: repeated);

        Assert.Throws<InvalidOperationException>(() => AddFight(evt, blue: repeated));
    }

    [Fact]
    public void AddFight_WithBothWeightClassAndCatchweight_Throws()
    {
        var evt = CreateEvent();

        Assert.Throws<ArgumentException>(() =>
            evt.AddFight(Guid.NewGuid(), Guid.NewGuid(), Discipline.K1, 3, 3,
                weightClass: "-72kg", catchweightKg: 74.5m, isTitleFight: false, isAmateur: false));
    }

    [Fact]
    public void AddFight_WithNeitherWeightClassNorCatchweight_Throws()
    {
        var evt = CreateEvent();

        Assert.Throws<ArgumentException>(() =>
            evt.AddFight(Guid.NewGuid(), Guid.NewGuid(), Discipline.K1, 3, 3,
                weightClass: null, catchweightKg: null, isTitleFight: false, isAmateur: false));
    }

    [Fact]
    public void MoveFight_Down_SwapsWithNextAndRederivesBilling()
    {
        var evt = CreateEvent();
        var f1 = AddFight(evt);
        var f2 = AddFight(evt);
        var f3 = AddFight(evt);

        var moved = evt.MoveFight(f2.Id, MoveDirection.Down);

        Assert.True(moved);
        Assert.Equal(3, f2.Order);
        Assert.Equal(2, f3.Order);
        Assert.Equal(FightBilling.Main, f2.Billing);
        Assert.Equal(FightBilling.CoMain, f3.Billing);
        Assert.Equal(FightBilling.Card, f1.Billing);
    }

    [Fact]
    public void MoveFight_UpAtFirstPosition_ReturnsFalse()
    {
        var evt = CreateEvent();
        var f1 = AddFight(evt);
        AddFight(evt);

        Assert.False(evt.MoveFight(f1.Id, MoveDirection.Up));
        Assert.Equal(1, f1.Order);
    }

    [Fact]
    public void MoveFight_UnknownFight_Throws()
    {
        var evt = CreateEvent();
        AddFight(evt);

        Assert.Throws<InvalidOperationException>(() => evt.MoveFight(Guid.NewGuid(), MoveDirection.Up));
    }

    [Fact]
    public void RemoveFight_ClosesOrderGapAndRederivesBilling()
    {
        var evt = CreateEvent();
        var f1 = AddFight(evt);
        var f2 = AddFight(evt);
        var f3 = AddFight(evt);

        var removed = evt.RemoveFight(f2.Id);

        Assert.True(removed);
        Assert.Equal(2, evt.Fights.Count);
        Assert.Equal(1, f1.Order);
        Assert.Equal(2, f3.Order);
        Assert.Equal(FightBilling.CoMain, f1.Billing);
        Assert.Equal(FightBilling.Main, f3.Billing);
    }

    [Fact]
    public void RemoveFight_Unknown_ReturnsFalse()
    {
        Assert.False(CreateEvent().RemoveFight(Guid.NewGuid()));
    }

    [Fact]
    public void ReplaceAthlete_OnScheduledFight_ReplacesCorner()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt);
        var substitute = Guid.NewGuid();

        evt.ReplaceAthlete(fight.Id, Corner.Blue, substitute);

        Assert.Equal(substitute, fight.BlueCornerAthleteId);
    }

    [Fact]
    public void ReplaceAthlete_WithAthleteAlreadyInCard_Throws()
    {
        var evt = CreateEvent();
        var other = AddFight(evt);
        var fight = AddFight(evt);

        Assert.Throws<InvalidOperationException>(() =>
            evt.ReplaceAthlete(fight.Id, Corner.Red, other.RedCornerAthleteId));
    }

    [Fact]
    public void HasAthlete_FindsAthleteInEitherCorner()
    {
        var evt = CreateEvent();
        var red = Guid.NewGuid();
        var blue = Guid.NewGuid();
        AddFight(evt, red: red, blue: blue);

        Assert.True(evt.HasAthlete(red));
        Assert.True(evt.HasAthlete(blue));
        Assert.False(evt.HasAthlete(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Domain.Tests --filter FightCardTests`
Expected: build error — `AddFight` does not exist.

- [ ] **Step 3: Add the card methods to Event.cs**

Insert into `src/Sfc.Domain/Events/Event.cs`, after `SetPoster` and before `SetDetails`:

```csharp
    public Fight AddFight(Guid redCornerAthleteId, Guid blueCornerAthleteId,
        Athletes.Discipline discipline, int rounds, int roundDurationMinutes,
        string? weightClass, decimal? catchweightKg, bool isTitleFight, bool isAmateur)
    {
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
        var fight = FindFight(fightId);
        EnsureAthleteNotInCard(newAthleteId);
        fight.ReplaceCorner(corner, newAthleteId);
        UpdatedAt = DateTime.UtcNow;
    }

    public bool HasAthlete(Guid athleteId)
        => _fights.Any(f => f.RedCornerAthleteId == athleteId || f.BlueCornerAthleteId == athleteId);

    private Fight FindFight(Guid fightId)
        => _fights.SingleOrDefault(f => f.Id == fightId)
            ?? throw new InvalidOperationException("Fight not found in this event.");

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
```

Also add `using Sfc.Domain.Athletes;` is NOT needed if `Discipline` is referenced as `Athletes.Discipline` in the signature — prefer adding the using and the bare `Discipline` name; both compile, pick the using + bare name for readability.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Domain.Tests --filter FightCardTests`
Expected: all PASS.

- [ ] **Step 5: Run the whole domain suite and commit**

Run: `dotnet test tests/Sfc.Domain.Tests`
Expected: all PASS.

```bash
git add src/Sfc.Domain/Events tests/Sfc.Domain.Tests/Events
git commit -m "Add fight card operations with derived billing to Event aggregate"
```

---

### Task 4: EF Core configuration and migration

**Files:**
- Modify: `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`
- Create: `src/Sfc.Infrastructure/Migrations/*_AddEventsAndFights.cs` (generated)
- Test: `tests/Sfc.Web.Tests/Persistence/EventFightPersistenceTests.cs`

**Interfaces:**
- Consumes: `Event`, `Fight` (Tasks 1–3).
- Produces: `DbSet<Event> SfcDbContext.Events`, `DbSet<Fight> SfcDbContext.Fights`; unique index `(OrganizationId, Slug)` on Events; cascade Event→Fights; `Restrict` FKs Fight→Athlete (both corners).

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/Sfc.Web.Tests/Persistence/EventFightPersistenceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests.Persistence;

public class EventFightPersistenceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Event_WithFights_RoundTripsOrderedWithAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = NewAthlete(db, "persist-red");
        var blue = NewAthlete(db, "persist-blue");
        db.Athletes.AddRange(red, blue);

        var evt = new Event(db.CurrentOrganizationId, "SFC Persist", new DateTime(2026, 10, 1, 20, 0, 0), "sfc-persist");
        evt.AddFight(red.Id, blue.Id, Discipline.K1, 3, 3, "-72kg", null, false, true);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Events
            .Include(e => e.Fights).ThenInclude(f => f.RedCornerAthlete)
            .SingleAsync(e => e.Id == evt.Id);

        var fight = Assert.Single(loaded.Fights);
        Assert.Equal(FightBilling.Main, fight.Billing);
        Assert.Equal("persist-red", fight.RedCornerAthlete!.Slug);
        Assert.Equal(new DateTime(2026, 10, 1, 20, 0, 0), loaded.Date);
    }

    [Fact]
    public async Task RemovingFightFromAggregate_DeletesRow()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = NewAthlete(db, "orphan-red");
        var blue = NewAthlete(db, "orphan-blue");
        db.Athletes.AddRange(red, blue);
        var evt = new Event(db.CurrentOrganizationId, "SFC Orphan", new DateTime(2026, 10, 2, 20, 0, 0), "sfc-orphan");
        var fight = evt.AddFight(red.Id, blue.Id, Discipline.Boxing, 4, 2, null, 68.5m, false, true);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        evt.RemoveFight(fight.Id);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Fights.CountAsync(f => f.EventId == evt.Id));
    }

    [Fact]
    public async Task DeletingAthleteWithFight_ThrowsRestrict()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = NewAthlete(db, "restrict-red");
        var blue = NewAthlete(db, "restrict-blue");
        db.Athletes.AddRange(red, blue);
        var evt = new Event(db.CurrentOrganizationId, "SFC Restrict", new DateTime(2026, 10, 3, 20, 0, 0), "sfc-restrict");
        evt.AddFight(red.Id, blue.Id, Discipline.Mma, 3, 5, "-77kg", null, false, false);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        db.Athletes.Remove(red);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Event_DuplicateSlugInSameOrganization_ThrowsOnSave()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        db.Events.Add(new Event(db.CurrentOrganizationId, "Dup A", new DateTime(2026, 10, 4), "dup-event-slug"));
        await db.SaveChangesAsync();
        db.Events.Add(new Event(db.CurrentOrganizationId, "Dup B", new DateTime(2026, 10, 5), "dup-event-slug"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Athlete NewAthlete(SfcDbContext db, string slug)
        => new(db.CurrentOrganizationId, "Atleta", slug, new DateOnly(1998, 3, 1), "Portugal",
            Discipline.K1, AthleteStatus.Amateur, slug);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventFightPersistenceTests`
Expected: build error — `SfcDbContext.Events` does not exist.

- [ ] **Step 3: Add DbSets and configuration**

In `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`: add `using Sfc.Domain.Events;`, add DbSets:

```csharp
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Fight> Fights => Set<Fight>();
```

In `OnModelCreating`, after the `Athlete` block and before `ApplyOrganizationQueryFilters(builder);`:

```csharp
        builder.Entity<Event>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.Date).HasColumnType("timestamp without time zone");
            entity.Property(e => e.Venue).HasMaxLength(200);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.BannerUrl).HasMaxLength(500);
            entity.Property(e => e.PosterUrl).HasMaxLength(500);
            entity.Property(e => e.TicketsUrl).HasMaxLength(500);
            entity.Property(e => e.StreamUrl).HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.OrganizationId, e.Slug }).IsUnique();
            entity.HasMany(e => e.Fights)
                .WithOne()
                .HasForeignKey(f => f.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(e => e.Fights)
                .HasField("_fights")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<Fight>(entity =>
        {
            entity.Property(f => f.Billing).HasConversion<string>().HasMaxLength(10);
            entity.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(f => f.Discipline).HasConversion<string>().HasMaxLength(20);
            entity.Property(f => f.WeightClass).HasMaxLength(50);
            entity.Property(f => f.CatchweightKg).HasPrecision(5, 2);
            entity.HasIndex(f => f.EventId);
            entity.HasOne(f => f.RedCornerAthlete)
                .WithMany()
                .HasForeignKey(f => f.RedCornerAthleteId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(f => f.BlueCornerAthlete)
                .WithMany()
                .HasForeignKey(f => f.BlueCornerAthleteId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 4: Generate the migration and verify content**

Run:
```bash
dotnet ef migrations add AddEventsAndFights -p src/Sfc.Infrastructure -s src/Sfc.Infrastructure
```
Expected: new `*_AddEventsAndFights.cs`. Verify it creates `Events` (with unique `IX_Events_OrganizationId_Slug`, `Date` as `timestamp without time zone`) and `Fights` (FK to Events `onDelete: Cascade`; FKs to Athletes `onDelete: Restrict`). Do not edit applied migrations.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventFightPersistenceTests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Infrastructure tests/Sfc.Web.Tests/Persistence
git commit -m "Add Events and Fights persistence with AddEventsAndFights migration"
```

---

### Task 5: AthleteService delete protection (HasFights) and athlete picker options

**Files:**
- Modify: `src/Sfc.Web/Services/AthleteService.cs`
- Modify: `src/Sfc.Web/Pages/Admin/Athletes/Delete.cshtml.cs` + `Delete.cshtml`
- Modify: `tests/Sfc.Web.Tests/Services/AthleteServiceTests.cs` (existing `DeleteAsync_RemovesAthlete` changes)
- Test: add cases to `tests/Sfc.Web.Tests/Services/AthleteServiceTests.cs`

**Interfaces:**
- Consumes: `db.Fights` (Task 4).
- Produces (used by Tasks 9–10):
  - `enum AthleteDeleteResult { Deleted, NotFound, HasFights }` (in AthleteService.cs)
  - `Task<AthleteDeleteResult> AthleteService.DeleteAsync(Guid id, CancellationToken ct = default)` — REPLACES the previous `Task<bool>` signature
  - `record AthleteOption(Guid Id, string Label)`
  - `Task<List<AthleteOption>> AthleteService.ListActiveOptionsAsync(string? name, Guid? clubId, Discipline? discipline, CancellationToken ct = default)` — active athletes ordered by LastName/FirstName, label `"Apelido, Nome 'Alcunha' — W-L-D · Clube"` (nickname/club parts omitted when null), same name/club/discipline filters as `SearchAsync`, no pagination.

- [ ] **Step 1: Write/adjust the failing tests**

In `tests/Sfc.Web.Tests/Services/AthleteServiceTests.cs`, change the existing delete test and add three:

```csharp
    [Fact]
    public async Task DeleteAsync_RemovesAthlete()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var athlete = await service.CreateAsync(Input("Para", "Apagar"), (0, 0, 0, 0), null);

        var result = await service.DeleteAsync(athlete.Id);

        Assert.Equal(AthleteDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(athlete.Id));
    }

    [Fact]
    public async Task DeleteAsync_AthleteWithFight_ReturnsHasFights()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = await service.CreateAsync(Input("Com", "Combate"), (0, 0, 0, 0), null);
        var blue = await service.CreateAsync(Input("Adversário", "Dele"), (0, 0, 0, 0), null);
        var evt = new Event(db.CurrentOrganizationId, "SFC Guard", new DateTime(2026, 11, 1, 20, 0, 0), "sfc-guard");
        evt.AddFight(red.Id, blue.Id, Discipline.MuayThai, 3, 3, "-72kg", null, false, false);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(red.Id);

        Assert.Equal(AthleteDeleteResult.HasFights, result);
        Assert.NotNull(await service.GetAsync(red.Id));
    }

    [Fact]
    public async Task ListActiveOptionsAsync_ReturnsLabelledActiveAthletesOnly()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var active = await service.CreateAsync(Input("Opção", "Ativa"), (3, 1, 0, 2), null);
        var inactive = await service.CreateAsync(Input("Opção", "Inativa"), (0, 0, 0, 0), null);
        await service.UpdateAsync(inactive.Id, Input("Opção", "Inativa"), isActive: false, null);

        var options = await service.ListActiveOptionsAsync("opção", null, null);

        Assert.Contains(options, o => o.Id == active.Id && o.Label.Contains("3-1-0"));
        Assert.DoesNotContain(options, o => o.Id == inactive.Id);
    }

    [Fact]
    public async Task ListActiveOptionsAsync_FiltersByDiscipline()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var boxer = await service.CreateAsync(Input("Filtro", "Boxe", discipline: Discipline.Boxing), (0, 0, 0, 0), null);

        var k1Options = await service.ListActiveOptionsAsync("Filtro Boxe", null, Discipline.K1);
        var boxingOptions = await service.ListActiveOptionsAsync("Filtro Boxe", null, Discipline.Boxing);

        Assert.DoesNotContain(k1Options, o => o.Id == boxer.Id);
        Assert.Contains(boxingOptions, o => o.Id == boxer.Id);
    }
```

Add the missing usings to the test file: `using Sfc.Domain.Events;` and `using Sfc.Infrastructure.Persistence;`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter AthleteServiceTests`
Expected: build error — `AthleteDeleteResult` does not exist.

- [ ] **Step 3: Implement in AthleteService**

In `src/Sfc.Web/Services/AthleteService.cs`, add next to the other records:

```csharp
public enum AthleteDeleteResult
{
    Deleted,
    NotFound,
    HasFights,
}

public record AthleteOption(Guid Id, string Label);
```

Replace `DeleteAsync` with:

```csharp
    public async Task<AthleteDeleteResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var athlete = await db.Athletes.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (athlete is null)
            return AthleteDeleteResult.NotFound;

        if (await db.Fights.AnyAsync(f => f.RedCornerAthleteId == id || f.BlueCornerAthleteId == id, ct))
            return AthleteDeleteResult.HasFights;

        db.Athletes.Remove(athlete);
        await db.SaveChangesAsync(ct);

        if (athlete.PhotoUrl is not null)
            await imageStorage.DeleteAsync($"athletes/{athlete.Id}.webp", ct);

        return AthleteDeleteResult.Deleted;
    }
```

Add `ListActiveOptionsAsync` (below `SearchAsync`, reusing its filter shape):

```csharp
    public async Task<List<AthleteOption>> ListActiveOptionsAsync(string? name, Guid? clubId,
        Discipline? discipline, CancellationToken ct = default)
    {
        var query = db.Athletes.AsNoTracking().Where(a => a.IsActive);

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

        var rows = await query
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.Nickname,
                ClubName = a.Club != null ? a.Club.Name : null,
                a.BaselineWins,
                a.BaselineLosses,
                a.BaselineDraws,
            })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var nickname = r.Nickname is null ? "" : $" '{r.Nickname}'";
                var club = r.ClubName is null ? "" : $" · {r.ClubName}";
                var label = $"{r.LastName}, {r.FirstName}{nickname} — " +
                    $"{r.BaselineWins}-{r.BaselineLosses}-{r.BaselineDraws}{club}";
                return new AthleteOption(r.Id, label);
            })
            .ToList();
    }
```

- [ ] **Step 4: Update the athlete Delete page**

`src/Sfc.Web/Pages/Admin/Athletes/Delete.cshtml.cs` — `OnPostAsync` becomes:

```csharp
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var result = await athleteService.DeleteAsync(id, ct);
        switch (result)
        {
            case AthleteDeleteResult.NotFound:
                return NotFound();
            case AthleteDeleteResult.HasFights:
                Athlete = await athleteService.GetAsync(id, ct);
                ModelState.AddModelError(string.Empty,
                    "Não é possível apagar um atleta com combates registados. Use antes o estado \"Inativo\".");
                return Page();
            default:
                TempData["Success"] = "Atleta apagado.";
                return RedirectToPage("Index");
        }
    }
```

In `Delete.cshtml`, add above the `<p>`: `<div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>`

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter AthleteServiceTests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Services
git commit -m "Protect athlete deletion when fights exist and add picker options"
```

---

### Task 6: EventService — CRUD, uploads, transitions, delete

**Files:**
- Create: `src/Sfc.Web/Services/EventService.cs`
- Modify: `src/Sfc.Web/Program.cs` (register)
- Modify: `src/Sfc.Web/Services/PtDisplay.cs` (EventStatus + FightBilling display names)
- Test: `tests/Sfc.Web.Tests/Services/EventServiceTests.cs`

**Interfaces:**
- Consumes: `Event` (Tasks 1/3), `SfcDbContext.Events` (Task 4), `IImageStorage`, `ImageProcessor`, `SlugGenerator`.
- Produces (used by Tasks 7–10):
  - `record EventInput(string Name, string? Description, DateTime Date, string? Venue, string? City, string? TicketsUrl, string? StreamUrl, string? Slug)`
  - `record EventListItem(Guid Id, string Name, DateTime Date, string? City, EventStatus Status, int FightCount)`
  - `enum EventDeleteResult { Deleted, NotFound, NotDeletable }`
  - `enum EventTransitionResult { Success, NotFound, InvalidTransition }`
  - `Task<List<EventListItem>> SearchAsync(string? name, EventStatus? status, CancellationToken ct = default)` — ordered by Date desc
  - `Task<Event?> GetWithCardAsync(Guid id, CancellationToken ct = default)` — includes ordered Fights + both corner athletes
  - `Task<Event> CreateAsync(EventInput input, Stream? banner, Stream? poster, CancellationToken ct = default)` — slug from name when blank, uniqueness suffix `-2`, `-3`…
  - `Task<Event?> UpdateAsync(Guid id, EventInput input, Stream? banner, Stream? poster, CancellationToken ct = default)` — slug change only applied while unpublished; a changed slug on a published event returns the event unchanged in that field (page guards it — the domain throws if the service tries)
  - `Task<EventTransitionResult> PublishAsync/UnpublishAsync/CompleteAsync/CancelAsync(Guid id, CancellationToken ct = default)`
  - `Task<EventDeleteResult> DeleteAsync(Guid id, CancellationToken ct = default)` — only Draft/Cancelled; removes banner/poster blobs
  - `PtDisplay.ToDisplay(EventStatus)`: Draft→"Rascunho", Published→"Publicado", Completed→"Concluído", Cancelled→"Cancelado"; `PtDisplay.ToDisplay(FightBilling)`: Main→"Combate principal", CoMain→"Co-main", Card→"Card"
  - Image constants: banner max 1920px → key `events/{id}-banner.webp`; poster max 1200px → key `events/{id}-poster.webp`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Services/EventServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Events;
using Sfc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class EventServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static EventInput Input(string name, string? slug = null)
        => new(name, "Descrição", new DateTime(2026, 11, 20, 20, 0, 0), "Pavilhão", "Lisboa",
            null, null, slug);

    [Fact]
    public async Task CreateAsync_GeneratesSlugAndDefaultsToDraft()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();

        var evt = await service.CreateAsync(Input("Gala de Verão"), null, null);

        Assert.Equal("gala-de-verao", evt.Slug);
        Assert.Equal(EventStatus.Draft, evt.Status);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_GetsNumericSuffix()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();

        await service.CreateAsync(Input("Evento Duplicado"), null, null);
        var second = await service.CreateAsync(Input("Evento Duplicado"), null, null);

        Assert.Equal("evento-duplicado-2", second.Slug);
    }

    [Fact]
    public async Task CreateAsync_WithBannerAndPoster_StoresWebps()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        using var banner = await CreatePngAsync(2400, 1000);
        using var poster = await CreatePngAsync(1400, 2000);

        var evt = await service.CreateAsync(Input("Com Imagens"), banner, poster);

        Assert.Equal($"https://media.test.local/events/{evt.Id}-banner.webp", evt.BannerUrl);
        Assert.Equal($"https://media.test.local/events/{evt.Id}-poster.webp", evt.PosterUrl);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"events/{evt.Id}-banner.webp"));
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"events/{evt.Id}-poster.webp"));
    }

    [Fact]
    public async Task Transitions_FollowTheStateMachine()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await service.CreateAsync(Input("Máquina de Estados"), null, null);

        Assert.Equal(EventTransitionResult.InvalidTransition, await service.CompleteAsync(evt.Id));
        Assert.Equal(EventTransitionResult.Success, await service.PublishAsync(evt.Id));
        Assert.Equal(EventTransitionResult.InvalidTransition, await service.PublishAsync(evt.Id));
        Assert.Equal(EventTransitionResult.Success, await service.CompleteAsync(evt.Id));
        Assert.Equal(EventTransitionResult.NotFound, await service.PublishAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_PublishedEvent_ReturnsNotDeletable()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await service.CreateAsync(Input("Não Apagável"), null, null);
        await service.PublishAsync(evt.Id);

        Assert.Equal(EventDeleteResult.NotDeletable, await service.DeleteAsync(evt.Id));
    }

    [Fact]
    public async Task DeleteAsync_DraftWithImages_DeletesRowAndBlobs()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        using var banner = await CreatePngAsync(1000, 400);
        var evt = await service.CreateAsync(Input("Apagável"), banner, null);

        var result = await service.DeleteAsync(evt.Id);

        Assert.Equal(EventDeleteResult.Deleted, result);
        Assert.False(factory.ImageStorage.Saved.ContainsKey($"events/{evt.Id}-banner.webp"));
        Assert.Null(await service.GetWithCardAsync(evt.Id));
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameAndStatus()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var published = await service.CreateAsync(Input("Pesquisável Publicado"), null, null);
        await service.PublishAsync(published.Id);
        await service.CreateAsync(Input("Pesquisável Rascunho"), null, null);

        var byName = await service.SearchAsync("pesquisável", null);
        var byStatus = await service.SearchAsync("Pesquisável", EventStatus.Published);

        Assert.True(byName.Count >= 2);
        Assert.Contains(byStatus, e => e.Id == published.Id);
        Assert.DoesNotContain(byStatus, e => e.Name == "Pesquisável Rascunho");
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

Run: `dotnet test tests/Sfc.Web.Tests --filter EventServiceTests`
Expected: build error — `EventService` does not exist.

- [ ] **Step 3: Implement EventService and register**

```csharp
// src/Sfc.Web/Services/EventService.cs
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
```

In `src/Sfc.Web/Program.cs`, next to the other services: `builder.Services.AddScoped<EventService>();`

In `src/Sfc.Web/Services/PtDisplay.cs`, add `using Sfc.Domain.Events;` and:

```csharp
    public static string ToDisplay(this EventStatus status) => status switch
    {
        EventStatus.Draft => "Rascunho",
        EventStatus.Published => "Publicado",
        EventStatus.Completed => "Concluído",
        EventStatus.Cancelled => "Cancelado",
        _ => status.ToString(),
    };

    public static string ToDisplay(this FightBilling billing) => billing switch
    {
        FightBilling.Main => "Combate principal",
        FightBilling.CoMain => "Co-main",
        FightBilling.Card => "Card",
        _ => billing.ToString(),
    };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventServiceTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Services
git commit -m "Add event service with uploads, transitions and guarded delete"
```

---

### Task 7: EventService — fight card operations

**Files:**
- Modify: `src/Sfc.Web/Services/EventService.cs`
- Test: `tests/Sfc.Web.Tests/Services/EventCardFlowTests.cs`

**Interfaces:**
- Consumes: `Event.AddFight/RemoveFight/MoveFight/ReplaceAthlete/HasAthlete` (Task 3), `GetWithCardAsync` (Task 6).
- Produces (used by Tasks 9–10):
  - `record FightInput(Guid RedCornerAthleteId, Guid BlueCornerAthleteId, Discipline Discipline, int Rounds, int RoundDurationMinutes, string? WeightClass, decimal? CatchweightKg, bool IsTitleFight, bool IsAmateur)`
  - `enum CardOperationResult { Success, EventNotFound, FightNotFound, AthleteAlreadyInCard, SameAthleteBothCorners, FightNotScheduled }`
  - `Task<CardOperationResult> AddFightAsync(Guid eventId, FightInput input, CancellationToken ct = default)`
  - `Task<CardOperationResult> RemoveFightAsync(Guid eventId, Guid fightId, CancellationToken ct = default)`
  - `Task<CardOperationResult> MoveFightAsync(Guid eventId, Guid fightId, MoveDirection direction, CancellationToken ct = default)` — no-op at the edges still returns `Success`
  - `Task<CardOperationResult> ReplaceAthleteAsync(Guid eventId, Guid fightId, Corner corner, Guid newAthleteId, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Services/EventCardFlowTests.cs
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class EventCardFlowTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput AthleteInput(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    private static FightInput Fight(Guid red, Guid blue)
        => new(red, blue, Discipline.MuayThai, 3, 3, "-72kg", null, false, false);

    [Fact]
    public async Task FullCardFlow_AddMoveReplaceRemove()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();

        var a1 = await athletes.CreateAsync(AthleteInput("Card", "Um"), (0, 0, 0, 0), null);
        var a2 = await athletes.CreateAsync(AthleteInput("Card", "Dois"), (0, 0, 0, 0), null);
        var a3 = await athletes.CreateAsync(AthleteInput("Card", "Três"), (0, 0, 0, 0), null);
        var a4 = await athletes.CreateAsync(AthleteInput("Card", "Quatro"), (0, 0, 0, 0), null);
        var a5 = await athletes.CreateAsync(AthleteInput("Card", "Cinco"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(
            new EventInput("Fluxo do Card", null, new DateTime(2026, 12, 5, 20, 0, 0),
                null, null, null, null, null), null, null);

        Assert.Equal(CardOperationResult.Success, await events.AddFightAsync(evt.Id, Fight(a1.Id, a2.Id)));
        Assert.Equal(CardOperationResult.Success, await events.AddFightAsync(evt.Id, Fight(a3.Id, a4.Id)));

        var card = (await events.GetWithCardAsync(evt.Id))!.Fights;
        Assert.Equal(FightBilling.CoMain, card[0].Billing);
        Assert.Equal(FightBilling.Main, card[1].Billing);

        Assert.Equal(CardOperationResult.Success,
            await events.MoveFightAsync(evt.Id, card[0].Id, MoveDirection.Down));
        var afterMove = (await events.GetWithCardAsync(evt.Id))!.Fights;
        Assert.Equal(card[0].Id, afterMove[1].Id);
        Assert.Equal(FightBilling.Main, afterMove[1].Billing);

        Assert.Equal(CardOperationResult.Success,
            await events.ReplaceAthleteAsync(evt.Id, afterMove[1].Id, Corner.Blue, a5.Id));
        var afterReplace = (await events.GetWithCardAsync(evt.Id))!.Fights;
        Assert.Equal(a5.Id, afterReplace[1].BlueCornerAthleteId);

        Assert.Equal(CardOperationResult.Success,
            await events.RemoveFightAsync(evt.Id, afterReplace[0].Id));
        var final = (await events.GetWithCardAsync(evt.Id))!.Fights;
        var remaining = Assert.Single(final);
        Assert.Equal(1, remaining.Order);
        Assert.Equal(FightBilling.Main, remaining.Billing);
    }

    [Fact]
    public async Task AddFightAsync_AthleteAlreadyInCard_ReturnsFriendlyResult()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var a1 = await athletes.CreateAsync(AthleteInput("Dup", "Um"), (0, 0, 0, 0), null);
        var a2 = await athletes.CreateAsync(AthleteInput("Dup", "Dois"), (0, 0, 0, 0), null);
        var a3 = await athletes.CreateAsync(AthleteInput("Dup", "Três"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(
            new EventInput("Card Duplicado", null, new DateTime(2026, 12, 6, 20, 0, 0),
                null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, Fight(a1.Id, a2.Id));

        Assert.Equal(CardOperationResult.AthleteAlreadyInCard,
            await events.AddFightAsync(evt.Id, Fight(a1.Id, a3.Id)));
        Assert.Equal(CardOperationResult.SameAthleteBothCorners,
            await events.AddFightAsync(evt.Id, Fight(a3.Id, a3.Id)));
    }

    [Fact]
    public async Task CardOperations_UnknownIds_ReturnNotFoundResults()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(
            new EventInput("Sem Combates", null, new DateTime(2026, 12, 7, 20, 0, 0),
                null, null, null, null, null), null, null);

        Assert.Equal(CardOperationResult.EventNotFound,
            await events.RemoveFightAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(CardOperationResult.FightNotFound,
            await events.RemoveFightAsync(evt.Id, Guid.NewGuid()));
        Assert.Equal(CardOperationResult.FightNotFound,
            await events.MoveFightAsync(evt.Id, Guid.NewGuid(), MoveDirection.Up));
    }
}
```

Note: `AthleteInput` gained a trailing `Notes` parameter in the previous branch (`string? Notes`) — the helper above passes `false, null, null` for `PublicProfileConsent, Slug, Notes`. If the record order differs, match `src/Sfc.Web/Services/AthleteService.cs` exactly.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventCardFlowTests`
Expected: build error — `FightInput` does not exist.

- [ ] **Step 3: Add card operations to EventService**

Append to `src/Sfc.Web/Services/EventService.cs` (records/enums next to the existing ones, methods inside the class):

```csharp
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
}
```

```csharp
    public async Task<CardOperationResult> AddFightAsync(Guid eventId, FightInput input,
        CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;
        if (input.RedCornerAthleteId == input.BlueCornerAthleteId)
            return CardOperationResult.SameAthleteBothCorners;
        if (evt.HasAthlete(input.RedCornerAthleteId) || evt.HasAthlete(input.BlueCornerAthleteId))
            return CardOperationResult.AthleteAlreadyInCard;

        evt.AddFight(input.RedCornerAthleteId, input.BlueCornerAthleteId, input.Discipline,
            input.Rounds, input.RoundDurationMinutes, input.WeightClass, input.CatchweightKg,
            input.IsTitleFight, input.IsAmateur);
        await db.SaveChangesAsync(ct);
        return CardOperationResult.Success;
    }

    public async Task<CardOperationResult> RemoveFightAsync(Guid eventId, Guid fightId,
        CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;

        if (!evt.RemoveFight(fightId))
            return CardOperationResult.FightNotFound;

        await db.SaveChangesAsync(ct);
        return CardOperationResult.Success;
    }

    public async Task<CardOperationResult> MoveFightAsync(Guid eventId, Guid fightId,
        MoveDirection direction, CancellationToken ct = default)
    {
        var evt = await GetTrackedWithFightsAsync(eventId, ct);
        if (evt is null)
            return CardOperationResult.EventNotFound;
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
        => db.Events.Include(e => e.Fights).SingleOrDefaultAsync(e => e.Id == id, ct);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventCardFlowTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Services
git commit -m "Add fight card operations to event service"
```

---

### Task 8: Events pages — Index, Create, Delete (+ navbar link)

**Files:**
- Create: `src/Sfc.Web/Pages/Admin/Events/Index.cshtml` + `Index.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Events/Create.cshtml` + `Create.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Events/Delete.cshtml` + `Delete.cshtml.cs`
- Modify: `src/Sfc.Web/Pages/Shared/_Layout.cshtml` (navbar: add Eventos link)
- Test: `tests/Sfc.Web.Tests/Pages/EventPagesTests.cs`

**Interfaces:**
- Consumes: `EventService` (Tasks 6–7), `AuthTestHelper`, `InvalidImageException`.
- Produces: pages at `/Admin/Events`, `/Admin/Events/Create`, `/Admin/Events/Delete/{id}`; shared form view-model `EventForm` (defined in `Create.cshtml.cs`, reused by Task 9's Edit).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Pages/EventPagesTests.cs
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class EventPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Index_ListsEventsWithStatusBadge()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        await service.CreateAsync(new EventInput("Evento Página Teste", null,
            new DateTime(2026, 12, 10, 20, 0, 0), null, "Lisboa", null, null, null), null, null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync("/Admin/Events");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("Evento Página Teste", html);
        Assert.Contains("Rascunho", html);
    }

    [Fact]
    public async Task Create_PostsFormAndRedirects()
    {
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Admin/Events/Create");

        var response = await client.PostAsync("/Admin/Events/Create", new MultipartFormDataContent
        {
            { new StringContent("Criado Via Form"), "Form.Name" },
            { new StringContent("2026-12-12T20:00"), "Form.Date" },
            { new StringContent("Lisboa"), "Form.City" },
            { new StringContent(token), "__RequestVerificationToken" },
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var results = await service.SearchAsync("Criado Via Form", null);
        var item = Assert.Single(results);
        Assert.Equal(new DateTime(2026, 12, 12, 20, 0, 0), item.Date);
    }

    [Theory]
    [InlineData("/Admin/Events/Create")]
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

Run: `dotnet test tests/Sfc.Web.Tests --filter EventPagesTests`
Expected: FAIL — `/Admin/Events` returns 404.

- [ ] **Step 3: Create the pages**

```csharp
// src/Sfc.Web/Pages/Admin/Events/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class IndexModel(EventService eventService) : PageModel
{
    public List<EventListItem> Events { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public EventStatus? Status { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
        => Events = await eventService.SearchAsync(Search, Status, ct);
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Index.cshtml *@
@page
@using Sfc.Domain.Events
@using Sfc.Web.Services
@model Sfc.Web.Pages.Admin.Events.IndexModel
@{
    ViewData["Title"] = "Eventos";
}
<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h3 mb-0">Eventos</h1>
    <a asp-page="Create" class="btn btn-primary">Novo evento</a>
</div>
<form method="get" class="row g-2 mb-3">
    <div class="col-12 col-md-5">
        <input asp-for="Search" class="form-control" placeholder="Pesquisar por nome…" />
    </div>
    <div class="col-6 col-md-3">
        <select asp-for="Status" class="form-select">
            <option value="">Todos os estados</option>
            @foreach (var s in Enum.GetValues<EventStatus>())
            {
                <option value="@s">@s.ToDisplay()</option>
            }
        </select>
    </div>
    <div class="col-auto">
        <button type="submit" class="btn btn-outline-secondary">Filtrar</button>
    </div>
</form>
@if (Model.Events.Count == 0)
{
    <p class="text-muted">Sem eventos para os filtros escolhidos.</p>
}
else
{
    <div class="table-responsive">
        <table class="table table-hover align-middle">
            <thead>
                <tr>
                    <th>Nome</th>
                    <th>Data</th>
                    <th class="d-none d-md-table-cell">Cidade</th>
                    <th class="d-none d-md-table-cell">Combates</th>
                    <th>Estado</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
            @foreach (var evt in Model.Events)
            {
                <tr>
                    <td>@evt.Name</td>
                    <td>@evt.Date.ToString("dd/MM/yyyy HH:mm")</td>
                    <td class="d-none d-md-table-cell">@evt.City</td>
                    <td class="d-none d-md-table-cell">@evt.FightCount</td>
                    <td>
                        <span class="badge @(evt.Status switch
                        {
                            EventStatus.Published => "text-bg-success",
                            EventStatus.Completed => "text-bg-secondary",
                            EventStatus.Cancelled => "text-bg-danger",
                            _ => "text-bg-warning",
                        })">@evt.Status.ToDisplay()</span>
                    </td>
                    <td class="text-end">
                        <a asp-page="Edit" asp-route-id="@evt.Id" class="btn btn-sm btn-outline-secondary">Editar</a>
                        <a asp-page="Delete" asp-route-id="@evt.Id" class="btn btn-sm btn-outline-danger">Apagar</a>
                    </td>
                </tr>
            }
            </tbody>
        </table>
    </div>
}
```

```csharp
// src/Sfc.Web/Pages/Admin/Events/Create.cshtml.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class EventForm
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(4000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "A data é obrigatória.")]
    public DateTime? Date { get; set; }

    [StringLength(200)] public string? Venue { get; set; }
    [StringLength(100)] public string? City { get; set; }

    [Url(ErrorMessage = "Link inválido.")]
    [StringLength(500)]
    public string? TicketsUrl { get; set; }

    [Url(ErrorMessage = "Link inválido.")]
    [StringLength(500)]
    public string? StreamUrl { get; set; }

    [StringLength(200)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug inválido: usar apenas minúsculas, números e hífens.")]
    public string? Slug { get; set; }

    public EventInput ToInput()
        => new(Name, Description, Date!.Value, Venue, City, TicketsUrl, StreamUrl, Slug);
}

public class CreateModel(EventService eventService) : PageModel
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    [BindProperty]
    public EventForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Banner { get; set; }

    [BindProperty]
    public IFormFile? Poster { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Banner is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Banner", "A imagem não pode exceder 10 MB.");
        if (Poster is { Length: > MaxUploadBytes })
            ModelState.AddModelError("Poster", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await using var bannerStream = Banner?.OpenReadStream();
            await using var posterStream = Poster?.OpenReadStream();
            var evt = await eventService.CreateAsync(Form.ToInput(), bannerStream, posterStream, ct);
            TempData["Success"] = "Evento criado com sucesso.";
            return RedirectToPage("Edit", new { id = evt.Id });
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Banner", "O ficheiro não é uma imagem válida.");
            return Page();
        }
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Create.cshtml *@
@page
@model Sfc.Web.Pages.Admin.Events.CreateModel
@{
    ViewData["Title"] = "Novo evento";
}
<h1 class="h3 mb-4">Novo evento</h1>
<form method="post" enctype="multipart/form-data" class="col-12 col-lg-8">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
    <div class="row">
        <div class="col-md-8 mb-3">
            <label asp-for="Form.Name" class="form-label">Nome *</label>
            <input asp-for="Form.Name" class="form-control" />
            <span asp-validation-for="Form.Name" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Date" class="form-label">Data e hora *</label>
            <input asp-for="Form.Date" type="datetime-local" class="form-control" />
            <span asp-validation-for="Form.Date" class="text-danger"></span>
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.Description" class="form-label">Descrição</label>
        <textarea asp-for="Form.Description" class="form-control" rows="3"></textarea>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.Venue" class="form-label">Local</label>
            <input asp-for="Form.Venue" class="form-control" />
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.City" class="form-label">Cidade</label>
            <input asp-for="Form.City" class="form-control" />
        </div>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.TicketsUrl" class="form-label">Link de bilhetes</label>
            <input asp-for="Form.TicketsUrl" class="form-control" />
            <span asp-validation-for="Form.TicketsUrl" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.StreamUrl" class="form-label">Link de streaming (YouTube)</label>
            <input asp-for="Form.StreamUrl" class="form-control" />
            <span asp-validation-for="Form.StreamUrl" class="text-danger"></span>
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.Slug" class="form-label">Slug (URL pública)</label>
        <input asp-for="Form.Slug" class="form-control" placeholder="gerado automaticamente do nome" />
        <span asp-validation-for="Form.Slug" class="text-danger"></span>
    </div>
    <div class="row">
        <div class="col-md-6 mb-4">
            <label asp-for="Banner" class="form-label">Banner</label>
            <input asp-for="Banner" type="file" accept="image/*" class="form-control" />
            <span asp-validation-for="Banner" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-4">
            <label asp-for="Poster" class="form-label">Poster</label>
            <input asp-for="Poster" type="file" accept="image/*" class="form-control" />
            <span asp-validation-for="Poster" class="text-danger"></span>
        </div>
    </div>
    <button type="submit" class="btn btn-primary">Guardar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

```csharp
// src/Sfc.Web/Pages/Admin/Events/Delete.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class DeleteModel(EventService eventService) : PageModel
{
    public Event? Event { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(id, ct);
        return Event is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var result = await eventService.DeleteAsync(id, ct);
        switch (result)
        {
            case EventDeleteResult.NotFound:
                return NotFound();
            case EventDeleteResult.NotDeletable:
                Event = await eventService.GetWithCardAsync(id, ct);
                ModelState.AddModelError(string.Empty,
                    "Só é possível apagar eventos em rascunho ou cancelados. Cancele o evento primeiro.");
                return Page();
            default:
                TempData["Success"] = "Evento apagado.";
                return RedirectToPage("Index");
        }
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Delete.cshtml *@
@page "{id:guid}"
@model Sfc.Web.Pages.Admin.Events.DeleteModel
@{
    ViewData["Title"] = "Apagar evento";
}
<h1 class="h3 mb-4">Apagar evento</h1>
<div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
<p>
    Tem a certeza que quer apagar o evento <strong>@Model.Event?.Name</strong>
    (@Model.Event?.Fights.Count combates no card)? Esta ação não pode ser desfeita.
</p>
<form method="post">
    <button type="submit" class="btn btn-danger">Apagar</button>
    <a asp-page="Index" class="btn btn-link">Cancelar</a>
</form>
```

In `src/Sfc.Web/Pages/Shared/_Layout.cshtml`, add after the Clubes nav item:

```cshtml
                <li class="nav-item"><a class="nav-link" asp-page="/Admin/Events/Index">Eventos</a></li>
```

Note: Task 9 creates `/Admin/Events/Edit`; until then the Index/Create pages reference it via `asp-page="Edit"` — create a placeholder `Edit.cshtml` in THIS task so links compile at runtime:

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Edit.cshtml — placeholder, replaced in Task 9 *@
@page "{id:guid}"
@{
    ViewData["Title"] = "Editar evento";
}
<h1 class="h3">Editar evento</h1>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventPagesTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Pages
git commit -m "Add events backoffice list, create and delete pages"
```

---

### Task 9: Event Edit page with fight card section and state transitions

**Files:**
- Modify: `src/Sfc.Web/Pages/Admin/Events/Edit.cshtml` (replace Task 8 placeholder) + create `Edit.cshtml.cs`
- Test: add to `tests/Sfc.Web.Tests/Pages/EventPagesTests.cs`

**Interfaces:**
- Consumes: `EventService` (Tasks 6–7), `EventForm` (Task 8), `PtDisplay` (Task 6).
- Produces: `/Admin/Events/Edit/{id}` with POST handlers `OnPostAsync` (save), `OnPostPublishAsync`, `OnPostUnpublishAsync`, `OnPostCompleteAsync`, `OnPostCancelAsync`, `OnPostMoveUpAsync(fightId)`, `OnPostMoveDownAsync(fightId)`, `OnPostRemoveFightAsync(fightId)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Sfc.Web.Tests/Pages/EventPagesTests.cs`:

```csharp
    [Fact]
    public async Task Edit_ShowsFightCardWithBillingAndAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(new AthleteInput("Edit", "Vermelho", null,
            new DateOnly(2000, 1, 1), "Portugal", Sfc.Domain.Athletes.Discipline.MuayThai,
            Sfc.Domain.Athletes.AthleteStatus.Professional, null, null, null, null, null,
            false, null, null), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(new AthleteInput("Edit", "Azul", null,
            new DateOnly(2000, 1, 1), "Portugal", Sfc.Domain.Athletes.Discipline.MuayThai,
            Sfc.Domain.Athletes.AthleteStatus.Professional, null, null, null, null, null,
            false, null, null), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Com Card", null,
            new DateTime(2026, 12, 15, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id,
            new FightInput(red.Id, blue.Id, Sfc.Domain.Athletes.Discipline.MuayThai,
                3, 3, "-72kg", null, false, false));

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var response = await client.GetAsync($"/Admin/Events/Edit/{evt.Id}");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("Edit Vermelho", html);
        Assert.Contains("Combate principal", html);
    }

    [Fact]
    public async Task Edit_PublishHandler_TransitionsAndShowsBadge()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(new EventInput("Evento Publicável", null,
            new DateTime(2026, 12, 16, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, $"/Admin/Events/Edit/{evt.Id}");
        var response = await client.PostAsync($"/Admin/Events/Edit/{evt.Id}?handler=Publish",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));
        var followUp = await client.GetAsync($"/Admin/Events/Edit/{evt.Id}");
        var html = System.Net.WebUtility.HtmlDecode(await followUp.Content.ReadAsStringAsync());

        Assert.Contains("Publicado", html);
    }
```

Add `using Sfc.Domain.Athletes;` won't be needed if fully qualified as above; keep the fully-qualified form to avoid ambiguity.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventPagesTests`
Expected: new tests FAIL (placeholder page has no card/handlers); Task 8 tests stay green.

- [ ] **Step 3: Implement the Edit page**

```csharp
// src/Sfc.Web/Pages/Admin/Events/Edit.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Images;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events;

public class EditModel(EventService eventService) : PageModel
{
    [BindProperty]
    public EventForm Form { get; set; } = new();

    [BindProperty]
    public IFormFile? Banner { get; set; }

    [BindProperty]
    public IFormFile? Poster { get; set; }

    public Event? Event { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(id, ct);
        if (Event is null)
            return NotFound();

        Form = new EventForm
        {
            Name = Event.Name,
            Description = Event.Description,
            Date = Event.Date,
            Venue = Event.Venue,
            City = Event.City,
            TicketsUrl = Event.TicketsUrl,
            StreamUrl = Event.StreamUrl,
            Slug = Event.Slug,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (Banner is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Banner", "A imagem não pode exceder 10 MB.");
        if (Poster is { Length: > CreateModel.MaxUploadBytes })
            ModelState.AddModelError("Poster", "A imagem não pode exceder 10 MB.");

        if (!ModelState.IsValid)
            return await ReloadAsync(id, ct);

        try
        {
            await using var bannerStream = Banner?.OpenReadStream();
            await using var posterStream = Poster?.OpenReadStream();
            var evt = await eventService.UpdateAsync(id, Form.ToInput(), bannerStream, posterStream, ct);
            if (evt is null)
                return NotFound();
        }
        catch (InvalidImageException)
        {
            ModelState.AddModelError("Banner", "O ficheiro não é uma imagem válida.");
            return await ReloadAsync(id, ct);
        }

        TempData["Success"] = "Evento atualizado.";
        return RedirectToPage(new { id });
    }

    public Task<IActionResult> OnPostPublishAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.PublishAsync(id, ct), "Evento publicado.", ct);

    public Task<IActionResult> OnPostUnpublishAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.UnpublishAsync(id, ct), "Evento despublicado.", ct);

    public Task<IActionResult> OnPostCompleteAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.CompleteAsync(id, ct), "Evento concluído.", ct);

    public Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken ct)
        => TransitionAsync(id, () => eventService.CancelAsync(id, ct), "Evento cancelado.", ct);

    public async Task<IActionResult> OnPostMoveUpAsync(Guid id, Guid fightId, CancellationToken ct)
    {
        await eventService.MoveFightAsync(id, fightId, MoveDirection.Up, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveDownAsync(Guid id, Guid fightId, CancellationToken ct)
    {
        await eventService.MoveFightAsync(id, fightId, MoveDirection.Down, ct);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveFightAsync(Guid id, Guid fightId, CancellationToken ct)
    {
        var result = await eventService.RemoveFightAsync(id, fightId, ct);
        TempData["Success"] = result == CardOperationResult.Success ? "Combate removido." : null;
        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> TransitionAsync(Guid id, Func<Task<EventTransitionResult>> action,
        string successMessage, CancellationToken ct)
    {
        var result = await action();
        switch (result)
        {
            case EventTransitionResult.NotFound:
                return NotFound();
            case EventTransitionResult.InvalidTransition:
                ModelState.AddModelError(string.Empty, "Transição de estado inválida.");
                return await ReloadAsync(id, ct);
            default:
                TempData["Success"] = successMessage;
                return RedirectToPage(new { id });
        }
    }

    private async Task<IActionResult> ReloadAsync(Guid id, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(id, ct);
        return Event is null ? NotFound() : Page();
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Edit.cshtml — replaces the Task 8 placeholder *@
@page "{id:guid}"
@using Sfc.Domain.Events
@using Sfc.Web.Services
@model Sfc.Web.Pages.Admin.Events.EditModel
@{
    ViewData["Title"] = "Editar evento";
    var evt = Model.Event!;
    var slugLocked = evt.PublishedAt is not null;
}
<div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h3 mb-0">@evt.Name</h1>
    <span class="badge fs-6 @(evt.Status switch
    {
        EventStatus.Published => "text-bg-success",
        EventStatus.Completed => "text-bg-secondary",
        EventStatus.Cancelled => "text-bg-danger",
        _ => "text-bg-warning",
    })">@evt.Status.ToDisplay()</span>
</div>
<div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>

<div class="mb-4">
    @if (evt.Status == EventStatus.Draft)
    {
        <form method="post" asp-page-handler="Publish" class="d-inline">
            <button type="submit" class="btn btn-success btn-sm">Publicar</button>
        </form>
    }
    @if (evt.Status == EventStatus.Published)
    {
        <form method="post" asp-page-handler="Unpublish" class="d-inline">
            <button type="submit" class="btn btn-outline-warning btn-sm">Despublicar</button>
        </form>
        <form method="post" asp-page-handler="Complete" class="d-inline">
            <button type="submit" class="btn btn-outline-secondary btn-sm">Concluir</button>
        </form>
    }
    @if (evt.Status is EventStatus.Draft or EventStatus.Published)
    {
        <form method="post" asp-page-handler="Cancel" class="d-inline"
              onsubmit="return confirm('Cancelar este evento?')">
            <button type="submit" class="btn btn-outline-danger btn-sm">Cancelar evento</button>
        </form>
    }
</div>

<form method="post" enctype="multipart/form-data" class="col-12 col-lg-8 mb-5">
    <div class="row">
        <div class="col-md-8 mb-3">
            <label asp-for="Form.Name" class="form-label">Nome *</label>
            <input asp-for="Form.Name" class="form-control" />
            <span asp-validation-for="Form.Name" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.Date" class="form-label">Data e hora *</label>
            <input asp-for="Form.Date" type="datetime-local" class="form-control" />
            <span asp-validation-for="Form.Date" class="text-danger"></span>
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.Description" class="form-label">Descrição</label>
        <textarea asp-for="Form.Description" class="form-control" rows="3"></textarea>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.Venue" class="form-label">Local</label>
            <input asp-for="Form.Venue" class="form-control" />
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.City" class="form-label">Cidade</label>
            <input asp-for="Form.City" class="form-control" />
        </div>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.TicketsUrl" class="form-label">Link de bilhetes</label>
            <input asp-for="Form.TicketsUrl" class="form-control" />
            <span asp-validation-for="Form.TicketsUrl" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.StreamUrl" class="form-label">Link de streaming (YouTube)</label>
            <input asp-for="Form.StreamUrl" class="form-control" />
            <span asp-validation-for="Form.StreamUrl" class="text-danger"></span>
        </div>
    </div>
    <div class="mb-3">
        <label asp-for="Form.Slug" class="form-label">Slug (URL pública)</label>
        <input asp-for="Form.Slug" class="form-control" readonly="@slugLocked" />
        <span asp-validation-for="Form.Slug" class="text-danger"></span>
        @if (slugLocked)
        {
            <div class="form-text">Imutável depois da primeira publicação.</div>
        }
    </div>
    <div class="row">
        <div class="col-md-6 mb-4">
            <label asp-for="Banner" class="form-label">Banner</label>
            @if (evt.BannerUrl is not null)
            {
                <div class="mb-2"><img src="@evt.BannerUrl" alt="Banner atual" class="img-fluid rounded" style="max-height:80px" /></div>
            }
            <input asp-for="Banner" type="file" accept="image/*" class="form-control" />
            <span asp-validation-for="Banner" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-4">
            <label asp-for="Poster" class="form-label">Poster</label>
            @if (evt.PosterUrl is not null)
            {
                <div class="mb-2"><img src="@evt.PosterUrl" alt="Poster atual" class="rounded" style="max-height:80px" /></div>
            }
            <input asp-for="Poster" type="file" accept="image/*" class="form-control" />
            <span asp-validation-for="Poster" class="text-danger"></span>
        </div>
    </div>
    <button type="submit" class="btn btn-primary">Guardar</button>
    <a asp-page="Index" class="btn btn-link">Voltar</a>
</form>

<div class="d-flex justify-content-between align-items-center mb-3">
    <h2 class="h4 mb-0">Fight card</h2>
    <a asp-page="Fights/Add" asp-route-eventId="@evt.Id" class="btn btn-primary btn-sm">Adicionar combate</a>
</div>
@if (evt.Fights.Count == 0)
{
    <p class="text-muted">Sem combates no card. O combate 1 abre a noite; o último é o combate principal.</p>
}
else
{
    <div class="table-responsive">
        <table class="table table-hover align-middle">
            <thead>
                <tr>
                    <th>#</th>
                    <th>Combate</th>
                    <th class="d-none d-md-table-cell">Disciplina</th>
                    <th class="d-none d-md-table-cell">Formato</th>
                    <th class="d-none d-md-table-cell">Peso</th>
                    <th>Billing</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
            @foreach (var fight in evt.Fights)
            {
                <tr>
                    <td>@fight.Order</td>
                    <td>
                        @fight.RedCornerAthlete?.FirstName @fight.RedCornerAthlete?.LastName
                        <span class="text-muted">vs</span>
                        @fight.BlueCornerAthlete?.FirstName @fight.BlueCornerAthlete?.LastName
                        @if (fight.IsTitleFight)
                        {
                            <span class="badge text-bg-warning">Título</span>
                        }
                        @if (fight.IsAmateur)
                        {
                            <span class="badge text-bg-info">Amador</span>
                        }
                    </td>
                    <td class="d-none d-md-table-cell">@fight.Discipline.ToDisplay()</td>
                    <td class="d-none d-md-table-cell">@fight.Rounds×@(fight.RoundDurationMinutes)min</td>
                    <td class="d-none d-md-table-cell">
                        @(fight.WeightClass ?? $"{fight.CatchweightKg}kg (combinado)")
                    </td>
                    <td>
                        <span class="badge @(fight.Billing switch
                        {
                            FightBilling.Main => "text-bg-danger",
                            FightBilling.CoMain => "text-bg-primary",
                            _ => "text-bg-secondary",
                        })">@fight.Billing.ToDisplay()</span>
                    </td>
                    <td class="text-end text-nowrap">
                        <form method="post" asp-page-handler="MoveUp" asp-route-fightId="@fight.Id" class="d-inline">
                            <button type="submit" class="btn btn-sm btn-outline-secondary" title="Mover para cima">↑</button>
                        </form>
                        <form method="post" asp-page-handler="MoveDown" asp-route-fightId="@fight.Id" class="d-inline">
                            <button type="submit" class="btn btn-sm btn-outline-secondary" title="Mover para baixo">↓</button>
                        </form>
                        @if (fight.Status == FightStatus.Scheduled)
                        {
                            <a asp-page="Fights/Replace" asp-route-eventId="@evt.Id" asp-route-fightId="@fight.Id"
                               class="btn btn-sm btn-outline-primary">Substituir</a>
                        }
                        <form method="post" asp-page-handler="RemoveFight" asp-route-fightId="@fight.Id"
                              class="d-inline" onsubmit="return confirm('Remover este combate do card?')">
                            <button type="submit" class="btn btn-sm btn-outline-danger">Remover</button>
                        </form>
                    </td>
                </tr>
            }
            </tbody>
        </table>
    </div>
}
```

Note: `readonly="@slugLocked"` — Razor renders the attribute only when true. Verify in the rendered HTML that a published event's slug input carries `readonly`; the domain/service still guard it server-side.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter EventPagesTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests/Pages
git commit -m "Add event edit page with fight card management and state transitions"
```

---

### Task 10: Add Fight and Replace Athlete pages

**Files:**
- Create: `src/Sfc.Web/Pages/Admin/Events/Fights/Add.cshtml` + `Add.cshtml.cs`
- Create: `src/Sfc.Web/Pages/Admin/Events/Fights/Replace.cshtml` + `Replace.cshtml.cs`
- Test: `tests/Sfc.Web.Tests/Pages/FightPagesTests.cs`

**Interfaces:**
- Consumes: `EventService.AddFightAsync/ReplaceAthleteAsync/GetWithCardAsync` + `FightInput` + `CardOperationResult` (Task 7), `AthleteService.ListActiveOptionsAsync` + `AthleteOption` (Task 5), `PtDisplay`.
- Produces: `/Admin/Events/Fights/Add/{eventId}` and `/Admin/Events/Fights/Replace/{eventId}/{fightId}`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Web.Tests/Pages/FightPagesTests.cs
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class FightPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput NewAthlete(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    [Fact]
    public async Task AddFight_PostsFormCreatesFightAndRedirectsToEdit()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete("Página", "Vermelha"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete("Página", "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Add Fight", null,
            new DateTime(2026, 12, 20, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client,
            $"/Admin/Events/Fights/Add/{evt.Id}");

        var response = await client.PostAsync($"/Admin/Events/Fights/Add/{evt.Id}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Form.RedCornerAthleteId", red.Id.ToString()),
                new KeyValuePair<string, string>("Form.BlueCornerAthleteId", blue.Id.ToString()),
                new KeyValuePair<string, string>("Form.Discipline", "MuayThai"),
                new KeyValuePair<string, string>("Form.Rounds", "3"),
                new KeyValuePair<string, string>("Form.RoundDurationMinutes", "3"),
                new KeyValuePair<string, string>("Form.WeightClass", "-72kg"),
                new KeyValuePair<string, string>("Form.IsTitleFight", "false"),
                new KeyValuePair<string, string>("Form.IsAmateur", "false"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var card = (await events.GetWithCardAsync(evt.Id))!.Fights;
        var fight = Assert.Single(card);
        Assert.Equal(red.Id, fight.RedCornerAthleteId);
    }

    [Fact]
    public async Task AddFight_WithBothWeightFields_ShowsValidationError()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete("XOR", "Vermelho"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete("XOR", "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento XOR", null,
            new DateTime(2026, 12, 21, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client,
            $"/Admin/Events/Fights/Add/{evt.Id}");

        var response = await client.PostAsync($"/Admin/Events/Fights/Add/{evt.Id}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Form.RedCornerAthleteId", red.Id.ToString()),
                new KeyValuePair<string, string>("Form.BlueCornerAthleteId", blue.Id.ToString()),
                new KeyValuePair<string, string>("Form.Discipline", "K1"),
                new KeyValuePair<string, string>("Form.Rounds", "3"),
                new KeyValuePair<string, string>("Form.RoundDurationMinutes", "3"),
                new KeyValuePair<string, string>("Form.WeightClass", "-72kg"),
                new KeyValuePair<string, string>("Form.CatchweightKg", "74.5"),
                new KeyValuePair<string, string>("Form.IsTitleFight", "false"),
                new KeyValuePair<string, string>("Form.IsAmateur", "false"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("categoria de peso OU peso combinado", html);
        Assert.Empty((await events.GetWithCardAsync(evt.Id))!.Fights);
    }

    [Fact]
    public async Task Replace_PostsFormAndSwapsAthlete()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete("Substituir", "Vermelho"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete("Substituir", "Azul"), (0, 0, 0, 0), null);
        var sub = await athletes.CreateAsync(NewAthlete("Substituto", "Novo"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Substituição", null,
            new DateTime(2026, 12, 22, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, new FightInput(red.Id, blue.Id,
            Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client,
            $"/Admin/Events/Fights/Replace/{evt.Id}/{fightId}");

        var response = await client.PostAsync($"/Admin/Events/Fights/Replace/{evt.Id}/{fightId}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Corner", "Blue"),
                new KeyValuePair<string, string>("NewAthleteId", sub.Id.ToString()),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var fight = (await events.GetWithCardAsync(evt.Id))!.Fights[0];
        Assert.Equal(sub.Id, fight.BlueCornerAthleteId);
        Assert.Equal(red.Id, fight.RedCornerAthleteId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sfc.Web.Tests --filter FightPagesTests`
Expected: FAIL — pages return 404.

- [ ] **Step 3: Implement the pages**

```csharp
// src/Sfc.Web/Pages/Admin/Events/Fights/Add.cshtml.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events.Fights;

public class FightForm
{
    [Required(ErrorMessage = "O canto vermelho é obrigatório.")]
    public Guid? RedCornerAthleteId { get; set; }

    [Required(ErrorMessage = "O canto azul é obrigatório.")]
    public Guid? BlueCornerAthleteId { get; set; }

    [Required(ErrorMessage = "A disciplina é obrigatória.")]
    public Discipline? Discipline { get; set; }

    [Range(1, 12, ErrorMessage = "Rounds entre 1 e 12.")]
    public int Rounds { get; set; } = 3;

    [Range(1, 10, ErrorMessage = "Duração entre 1 e 10 minutos.")]
    public int RoundDurationMinutes { get; set; } = 3;

    [StringLength(50)]
    public string? WeightClass { get; set; }

    [Range(20, 200, ErrorMessage = "Peso combinado entre 20 e 200 kg.")]
    public decimal? CatchweightKg { get; set; }

    public bool IsTitleFight { get; set; }
    public bool IsAmateur { get; set; }

    public FightInput ToInput()
        => new(RedCornerAthleteId!.Value, BlueCornerAthleteId!.Value, Discipline!.Value,
            Rounds, RoundDurationMinutes, WeightClass, CatchweightKg, IsTitleFight, IsAmateur);
}

public class AddModel(EventService eventService, AthleteService athleteService,
    ClubService clubService) : PageModel
{
    [BindProperty]
    public FightForm Form { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FilterName { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? FilterClubId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Discipline? FilterDiscipline { get; set; }

    public Event? Event { get; private set; }
    public List<SelectListItem> AthleteOptions { get; private set; } = [];
    public List<SelectListItem> ClubOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid eventId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, ct))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid eventId, CancellationToken ct)
    {
        ValidateWeightXor();
        if (Form.RedCornerAthleteId is not null && Form.RedCornerAthleteId == Form.BlueCornerAthleteId)
            ModelState.AddModelError("Form.BlueCornerAthleteId",
                "O mesmo atleta não pode estar nos dois cantos.");

        if (!ModelState.IsValid)
            return await ReloadAsync(eventId, ct);

        var result = await eventService.AddFightAsync(eventId, Form.ToInput(), ct);
        switch (result)
        {
            case CardOperationResult.EventNotFound:
                return NotFound();
            case CardOperationResult.AthleteAlreadyInCard:
                ModelState.AddModelError(string.Empty,
                    "Um dos atletas já tem combate neste evento.");
                return await ReloadAsync(eventId, ct);
            case CardOperationResult.SameAthleteBothCorners:
                ModelState.AddModelError(string.Empty,
                    "O mesmo atleta não pode estar nos dois cantos.");
                return await ReloadAsync(eventId, ct);
            default:
                TempData["Success"] = "Combate adicionado ao card.";
                return RedirectToPage("/Admin/Events/Edit", new { id = eventId });
        }
    }

    private void ValidateWeightXor()
    {
        var hasWeightClass = !string.IsNullOrWhiteSpace(Form.WeightClass);
        if (hasWeightClass == Form.CatchweightKg.HasValue)
            ModelState.AddModelError("Form.WeightClass",
                "Indique a categoria de peso OU peso combinado (exatamente um).");
    }

    private async Task<IActionResult> ReloadAsync(Guid eventId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, ct))
            return NotFound();
        return Page();
    }

    private async Task<bool> LoadAsync(Guid eventId, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(eventId, ct);
        if (Event is null)
            return false;

        AthleteOptions = (await athleteService.ListActiveOptionsAsync(FilterName, FilterClubId, FilterDiscipline, ct))
            .Select(o => new SelectListItem(o.Label, o.Id.ToString()))
            .ToList();
        ClubOptions = (await clubService.SearchAsync(null, ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
        return true;
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Fights/Add.cshtml *@
@page "{eventId:guid}"
@using Sfc.Domain.Athletes
@using Sfc.Web.Services
@model Sfc.Web.Pages.Admin.Events.Fights.AddModel
@{
    ViewData["Title"] = "Adicionar combate";
}
<h1 class="h3 mb-1">Adicionar combate</h1>
<p class="text-muted mb-4">@Model.Event?.Name</p>

<form method="get" class="row g-2 mb-3">
    <input type="hidden" name="eventId" value="@Model.Event?.Id" />
    <div class="col-12 col-md-4">
        <input asp-for="FilterName" class="form-control" placeholder="Filtrar atletas por nome…" />
    </div>
    <div class="col-6 col-md-3">
        <select asp-for="FilterClubId" asp-items="Model.ClubOptions" class="form-select">
            <option value="">Todos os clubes</option>
        </select>
    </div>
    <div class="col-6 col-md-3">
        <select asp-for="FilterDiscipline" class="form-select">
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

<form method="post" class="col-12 col-lg-8">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.RedCornerAthleteId" class="form-label">Canto vermelho *</label>
            <select asp-for="Form.RedCornerAthleteId" asp-items="Model.AthleteOptions" class="form-select">
                <option value="">Escolher atleta…</option>
            </select>
            <span asp-validation-for="Form.RedCornerAthleteId" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.BlueCornerAthleteId" class="form-label">Canto azul *</label>
            <select asp-for="Form.BlueCornerAthleteId" asp-items="Model.AthleteOptions" class="form-select">
                <option value="">Escolher atleta…</option>
            </select>
            <span asp-validation-for="Form.BlueCornerAthleteId" class="text-danger"></span>
        </div>
    </div>
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
            <label asp-for="Form.Rounds" class="form-label">Rounds</label>
            <input asp-for="Form.Rounds" type="number" class="form-control" />
            <span asp-validation-for="Form.Rounds" class="text-danger"></span>
        </div>
        <div class="col-md-4 mb-3">
            <label asp-for="Form.RoundDurationMinutes" class="form-label">Minutos por round</label>
            <input asp-for="Form.RoundDurationMinutes" type="number" class="form-control" />
            <span asp-validation-for="Form.RoundDurationMinutes" class="text-danger"></span>
            <div class="form-text">Amador: tipicamente 3×2; profissional: 3×3 (MMA 5 min).</div>
        </div>
    </div>
    <div class="row">
        <div class="col-md-6 mb-3">
            <label asp-for="Form.WeightClass" class="form-label">Categoria de peso</label>
            <input asp-for="Form.WeightClass" class="form-control" placeholder="ex.: -72kg" />
            <span asp-validation-for="Form.WeightClass" class="text-danger"></span>
        </div>
        <div class="col-md-6 mb-3">
            <label asp-for="Form.CatchweightKg" class="form-label">Peso combinado (kg)</label>
            <input asp-for="Form.CatchweightKg" type="number" step="0.1" class="form-control" />
            <span asp-validation-for="Form.CatchweightKg" class="text-danger"></span>
            <div class="form-text">Preencher apenas um: categoria OU peso combinado.</div>
        </div>
    </div>
    <div class="form-check mb-2">
        <input asp-for="Form.IsTitleFight" class="form-check-input" />
        <label asp-for="Form.IsTitleFight" class="form-check-label">Combate de título</label>
    </div>
    <div class="form-check mb-4">
        <input asp-for="Form.IsAmateur" class="form-check-input" />
        <label asp-for="Form.IsAmateur" class="form-check-label">Amador</label>
    </div>
    <button type="submit" class="btn btn-primary">Adicionar</button>
    <a asp-page="/Admin/Events/Edit" asp-route-id="@Model.Event?.Id" class="btn btn-link">Cancelar</a>
</form>
```

```csharp
// src/Sfc.Web/Pages/Admin/Events/Fights/Replace.cshtml.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;

namespace Sfc.Web.Pages.Admin.Events.Fights;

public class ReplaceModel(EventService eventService, AthleteService athleteService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Escolha o canto a substituir.")]
    public Corner? Corner { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Escolha o novo atleta.")]
    public Guid? NewAthleteId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterName { get; set; }

    public Event? Event { get; private set; }
    public Fight? Fight { get; private set; }
    public List<SelectListItem> AthleteOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return await ReloadAsync(eventId, fightId, ct);

        var result = await eventService.ReplaceAthleteAsync(eventId, fightId, Corner!.Value,
            NewAthleteId!.Value, ct);
        switch (result)
        {
            case CardOperationResult.EventNotFound:
            case CardOperationResult.FightNotFound:
                return NotFound();
            case CardOperationResult.FightNotScheduled:
                ModelState.AddModelError(string.Empty,
                    "Só é possível substituir atletas em combates agendados.");
                return await ReloadAsync(eventId, fightId, ct);
            case CardOperationResult.AthleteAlreadyInCard:
                ModelState.AddModelError(string.Empty,
                    "O atleta escolhido já tem combate neste evento.");
                return await ReloadAsync(eventId, fightId, ct);
            default:
                TempData["Success"] = "Atleta substituído.";
                return RedirectToPage("/Admin/Events/Edit", new { id = eventId });
        }
    }

    private async Task<IActionResult> ReloadAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        if (!await LoadAsync(eventId, fightId, ct))
            return NotFound();
        return Page();
    }

    private async Task<bool> LoadAsync(Guid eventId, Guid fightId, CancellationToken ct)
    {
        Event = await eventService.GetWithCardAsync(eventId, ct);
        Fight = Event?.Fights.SingleOrDefault(f => f.Id == fightId);
        if (Event is null || Fight is null)
            return false;

        AthleteOptions = (await athleteService.ListActiveOptionsAsync(FilterName, null, null, ct))
            .Select(o => new SelectListItem(o.Label, o.Id.ToString()))
            .ToList();
        return true;
    }
}
```

```cshtml
@* src/Sfc.Web/Pages/Admin/Events/Fights/Replace.cshtml *@
@page "{eventId:guid}/{fightId:guid}"
@using Sfc.Domain.Events
@model Sfc.Web.Pages.Admin.Events.Fights.ReplaceModel
@{
    ViewData["Title"] = "Substituir atleta";
    var fight = Model.Fight!;
}
<h1 class="h3 mb-1">Substituir atleta</h1>
<p class="text-muted mb-4">
    @Model.Event?.Name — combate @fight.Order:
    @fight.RedCornerAthlete?.FirstName @fight.RedCornerAthlete?.LastName
    vs @fight.BlueCornerAthlete?.FirstName @fight.BlueCornerAthlete?.LastName
</p>

<form method="get" class="row g-2 mb-3">
    <input type="hidden" name="eventId" value="@Model.Event?.Id" />
    <input type="hidden" name="fightId" value="@fight.Id" />
    <div class="col-12 col-md-4">
        <input asp-for="FilterName" class="form-control" placeholder="Filtrar atletas por nome…" />
    </div>
    <div class="col-auto">
        <button type="submit" class="btn btn-outline-secondary">Filtrar</button>
    </div>
</form>

<form method="post" class="col-12 col-md-8 col-lg-6">
    <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
    <div class="mb-3">
        <label class="form-label d-block">Canto a substituir *</label>
        <div class="form-check form-check-inline">
            <input asp-for="Corner" type="radio" value="@Corner.Red" class="form-check-input" id="cornerRed" />
            <label class="form-check-label" for="cornerRed">
                Canto vermelho (@fight.RedCornerAthlete?.FirstName @fight.RedCornerAthlete?.LastName)
            </label>
        </div>
        <div class="form-check form-check-inline">
            <input asp-for="Corner" type="radio" value="@Corner.Blue" class="form-check-input" id="cornerBlue" />
            <label class="form-check-label" for="cornerBlue">
                Canto azul (@fight.BlueCornerAthlete?.FirstName @fight.BlueCornerAthlete?.LastName)
            </label>
        </div>
        <span asp-validation-for="Corner" class="text-danger d-block"></span>
    </div>
    <div class="mb-4">
        <label asp-for="NewAthleteId" class="form-label">Novo atleta *</label>
        <select asp-for="NewAthleteId" asp-items="Model.AthleteOptions" class="form-select">
            <option value="">Escolher atleta…</option>
        </select>
        <span asp-validation-for="NewAthleteId" class="text-danger"></span>
    </div>
    <button type="submit" class="btn btn-primary">Substituir</button>
    <a asp-page="/Admin/Events/Edit" asp-route-id="@Model.Event?.Id" class="btn btn-link">Cancelar</a>
</form>
```

Note the `@using Sfc.Domain.Events` in Replace.cshtml so `Corner.Red`/`Corner.Blue` resolve to the domain enum (the page property is also named `Corner` — if the compiler resolves the identifier to the property, qualify as `Sfc.Domain.Events.Corner.Red` in the two `value=` attributes).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sfc.Web.Tests --filter FightPagesTests`
Expected: all PASS.

- [ ] **Step 5: Run the full solution suite and commit**

Run: `dotnet test`
Expected: all projects PASS.

```bash
git add src/Sfc.Web tests/Sfc.Web.Tests
git commit -m "Add fight card add and replace athlete pages"
```

---

### Task 11: Final verification and quality gates

**Files:**
- No new code — verification, agents, and PR preparation.

- [ ] **Step 1: Full build and test run**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, all tests PASS.

- [ ] **Step 2: Manual smoke test (browser + real MinIO)**

`docker compose up -d --wait`, run the app with seed admin args, then in the browser: create an event with banner/poster (verify WebP served from MinIO), add 3 fights (verify billing badges: 1=Card, 2=Co-main, 3=Combate principal), reorder with ↑/↓, replace an athlete, remove a fight, publish → verify slug becomes readonly, cancel → delete. Clean up smoke data from the dev DB afterwards.

- [ ] **Step 3: Run gates**

Dispatch `guardiao-ambito` (scope: itens 3–4 do backoffice; explicitly OUT: resultados, pesagens, portal, PDFs, matchmaking) and `revisor-dominio` (billing derivation, fight formats, substituição, vocabulário pt-PT) over the branch. Fix findings. Then run the final whole-branch code review (superpowers) and `/security-review`. Fix findings.

- [ ] **Step 4: Push and open PR**

```bash
git push -u origin feature/eventos-fightcard
```

Create the PR with `gh` ("Add events and fight card backoffice management"), including the gates checklist and any notes for Caio. CI green before merge.

