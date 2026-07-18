# Results and Records Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record fight results and keep athlete records automatically up to date (displayed record = baseline + aggregation of platform results), with corrections/deletions reverting and reapplying effects in one transaction, per `docs/plans/2026-07-18-resultados-records-design.md`.

**Architecture:** `FightResult` is created/mutated only through the `Event` aggregate (via `Fight`). The result computes per-athlete `RecordDelta`s from the method-effects matrix; `Athlete` stores aggregated result counters (`ResultWins/…`) and exposes `Wins = BaselineWins + ResultWins`. `EventService` loads the aggregate plus the two corner athletes and applies deltas in a single `SaveChanges` (one transaction).

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + Testcontainers PostgreSQL, Razor Pages.

## Global Constraints

- Code, entities, tests in **English**; user-visible strings in **pt-PT** (CLAUDE.md rule 4).
- Records are NEVER edited directly (domain rule 3); only baseline at creation + result deltas.
- Method effects (skill sfc-contexto): KO/TKO → winner +1W +1KO, loser +1L; decisions → +1W/+1L; Draw → +1D both; No Contest → no change; DQ → +1W/+1L; Forfeit → +1W/+1L.
- Round/Time: KO/TKO round required (1..fight.Rounds), time optional (`m:ss`, seconds < 60); DQ round/time optional; decisions/Draw/NC/Forfeit must have neither.
- Winner required and ∈ {red, blue} for all methods except Draw/NC (winner must be null).
- Results only when event `Date.Date <= today` (today = Europe/Lisbon, computed at the service boundary) and event not `Cancelled`; correction/deletion have no date check.
- `Fight` mutators and `FightResult` ctor are `internal` — everything goes through `Event`.
- No repositories, no MediatR (ADR-001). `TreatWarningsAsErrors` is on.
- Branch `feature/resultados-records`; never push to `master`.
- Migrations: `dotnet ef migrations add <Name> -p src/Sfc.Infrastructure -s src/Sfc.Infrastructure`.

---

### Task 1: RecordDelta and Athlete result aggregation (domain)

**Files:**
- Create: `src/Sfc.Domain/Athletes/RecordDelta.cs`
- Modify: `src/Sfc.Domain/Athletes/Athlete.cs` (result counters + `ApplyResultDelta`; computed record)
- Test: `tests/Sfc.Domain.Tests/Athletes/AthleteRecordTests.cs`

**Interfaces:**
- Produces: `record RecordDelta(int Wins, int Losses, int Draws, int Kos)` with `static RecordDelta Zero` and `RecordDelta Negate()`; `void Athlete.ApplyResultDelta(RecordDelta delta)` (throws `InvalidOperationException` if any counter would go negative); `Athlete.Wins/Losses/Draws/WinsByKo` become baseline + result counters; new persisted ints `ResultWins/ResultLosses/ResultDraws/ResultKos` (private set, default 0).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Sfc.Domain.Tests/Athletes/AthleteRecordTests.cs
using Sfc.Domain.Athletes;
using Xunit;

namespace Sfc.Domain.Tests.Athletes;

public class AthleteRecordTests
{
    private static Athlete CreateAthlete(int wins = 10, int losses = 2, int draws = 1, int kos = 4)
        => new(Guid.NewGuid(), "Ana", "Silva", new DateOnly(1999, 5, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "ana-silva",
            baselineWins: wins, baselineLosses: losses, baselineDraws: draws, baselineKos: kos);

    [Fact]
    public void Record_IsBaselinePlusResultAggregation()
    {
        var athlete = CreateAthlete();

        athlete.ApplyResultDelta(new RecordDelta(1, 0, 0, 1)); // KO win
        athlete.ApplyResultDelta(new RecordDelta(0, 1, 0, 0)); // loss
        athlete.ApplyResultDelta(new RecordDelta(0, 0, 1, 0)); // draw

        Assert.Equal(11, athlete.Wins);
        Assert.Equal(3, athlete.Losses);
        Assert.Equal(2, athlete.Draws);
        Assert.Equal(5, athlete.WinsByKo);
        Assert.Equal("11-3-2", athlete.RecordDisplay);
    }

    [Fact]
    public void ApplyResultDelta_NegatedDelta_RestoresPreviousRecord()
    {
        var athlete = CreateAthlete();
        var delta = new RecordDelta(1, 0, 0, 1);

        athlete.ApplyResultDelta(delta);
        athlete.ApplyResultDelta(delta.Negate());

        Assert.Equal(10, athlete.Wins);
        Assert.Equal(4, athlete.WinsByKo);
    }

    [Fact]
    public void ApplyResultDelta_ThatWouldGoNegative_Throws()
    {
        var athlete = CreateAthlete();

        Assert.Throws<InvalidOperationException>(() =>
            athlete.ApplyResultDelta(new RecordDelta(-1, 0, 0, 0)));
    }

    [Fact]
    public void ZeroDelta_ChangesNothing()
    {
        var athlete = CreateAthlete();

        athlete.ApplyResultDelta(RecordDelta.Zero);

        Assert.Equal("10-2-1", athlete.RecordDisplay);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test tests/Sfc.Domain.Tests --filter AthleteRecordTests`; expected: build error (`RecordDelta` missing).

- [ ] **Step 3: Implement**

```csharp
// src/Sfc.Domain/Athletes/RecordDelta.cs
namespace Sfc.Domain.Athletes;

/// <summary>Per-athlete effect of one fight result on the record (see sfc-contexto matrix).</summary>
public record RecordDelta(int Wins, int Losses, int Draws, int Kos)
{
    public static readonly RecordDelta Zero = new(0, 0, 0, 0);

    public RecordDelta Negate() => new(-Wins, -Losses, -Draws, -Kos);
}
```

In `Athlete.cs`: add persisted counters + method, and change the computed record:

```csharp
    // Aggregated from platform FightResults; mutated only via ApplyResultDelta
    // (domain rule 3 — displayed record = baseline + aggregation).
    public int ResultWins { get; private set; }
    public int ResultLosses { get; private set; }
    public int ResultDraws { get; private set; }
    public int ResultKos { get; private set; }

    public int Wins => BaselineWins + ResultWins;
    public int Losses => BaselineLosses + ResultLosses;
    public int Draws => BaselineDraws + ResultDraws;
    public int WinsByKo => BaselineKos + ResultKos;

    public void ApplyResultDelta(RecordDelta delta)
    {
        var wins = ResultWins + delta.Wins;
        var losses = ResultLosses + delta.Losses;
        var draws = ResultDraws + delta.Draws;
        var kos = ResultKos + delta.Kos;
        if (wins < 0 || losses < 0 || draws < 0 || kos < 0)
            throw new InvalidOperationException("Result counters cannot go negative.");

        ResultWins = wins;
        ResultLosses = losses;
        ResultDraws = draws;
        ResultKos = kos;
        UpdatedAt = DateTime.UtcNow;
    }
```

- [ ] **Step 4: Run tests** — same filter; expected: PASS. Also run the full domain suite.
- [ ] **Step 5: Commit** — `git commit -m "Add athlete record aggregation with RecordDelta"`

---

### Task 2: FightResult, method effects, and Event result operations (domain)

**Files:**
- Create: `src/Sfc.Domain/Events/FightResultMethod.cs`
- Create: `src/Sfc.Domain/Events/FightResult.cs`
- Modify: `src/Sfc.Domain/Events/Fight.cs` (Result nav + internal result/status mutators)
- Modify: `src/Sfc.Domain/Events/Event.cs` (public result operations)
- Test: `tests/Sfc.Domain.Tests/Events/FightResultTests.cs`

**Interfaces:**
- Consumes: `RecordDelta` (Task 1).
- Produces:
  - `enum FightResultMethod { Ko, Tko, UnanimousDecision, SplitDecision, MajorityDecision, Draw, NoContest, Disqualification, Forfeit }`
  - `FightResult` (public read-only props `Id, OrganizationId, FightId, WinnerAthleteId, Method, Round, Time, CreatedAt, UpdatedAt`); `(RecordDelta Red, RecordDelta Blue) GetDeltas(Guid redAthleteId, Guid blueAthleteId)`
  - `Fight.Result` (`FightResult?`)
  - On `Event`: `FightResult RecordResult(Guid fightId, Guid? winnerAthleteId, FightResultMethod method, int? round, string? time, DateOnly today)`; `FightResult ChangeResult(Guid fightId, Guid? winnerAthleteId, FightResultMethod method, int? round, string? time)`; `void DeleteResult(Guid fightId)`; `void CancelFight(Guid fightId)`; `void MarkFightNoContest(Guid fightId)`; `void ReinstateFight(Guid fightId)`
  - Status rules: RecordResult → `Completed` (NC method → `NoContest`); DeleteResult → `Scheduled`; Cancel/NoContest only from `Scheduled`; Reinstate only from `Cancelled`/`NoContest` without result.
  - Validation: winner/method coherence; winner ∈ corners; round/time table; round ≤ fight.Rounds; time `m:ss` seconds < 60. Invalid input → `ArgumentException`; invalid state → `InvalidOperationException`.

- [ ] **Step 1: Write the failing tests** — cover, via `Event` (fixture: event dated 2026-07-01, `today` = 2026-07-18):
  - effects matrix per method (red winner, blue winner where relevant): KO/TKO deltas `(1,0,0,1)/(0,1,0,0)`; each decision `(1,0,0,0)/(0,1,0,0)`; DQ and Forfeit same as decision; Draw `(0,0,1,0)` both; NC `Zero` both
  - winner validation: Draw/NC with winner → throws; KO without winner → throws; winner not in corners → throws
  - round/time: KO without round → throws; round > fight.Rounds → throws; decision with round → throws; Forfeit with time → throws; KO with time `"1:75"` or `"abc"` → throws; KO with round only (no time) → ok
  - state transitions: RecordResult sets Completed (NC method sets NoContest) and `Fight.Result`; RecordResult on non-Scheduled → throws; on cancelled event → throws; event date after `today` → throws
  - ChangeResult replaces values and re-derives status (KO→NC and NC→KO); ChangeResult without result → throws
  - DeleteResult → `Result` null, status Scheduled; without result → throws
  - CancelFight/MarkFightNoContest from Scheduled → status set; from Completed → throws
  - ReinstateFight from Cancelled → Scheduled; from NoContest-with-result → throws; from Scheduled → throws

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Sfc.Domain.Tests --filter FightResultTests`; expected: build error.

- [ ] **Step 3: Implement**

```csharp
// src/Sfc.Domain/Events/FightResultMethod.cs
namespace Sfc.Domain.Events;

public enum FightResultMethod
{
    Ko,
    Tko,
    UnanimousDecision,
    SplitDecision,
    MajorityDecision,
    Draw,
    NoContest,
    Disqualification,
    Forfeit,
}
```

```csharp
// src/Sfc.Domain/Events/FightResult.cs (essentials)
public class FightResult : IOrganizationScoped
{
    // props: Id, OrganizationId, FightId, WinnerAthleteId?, Method, Round?, Time?, CreatedAt, UpdatedAt
    internal FightResult(Fight fight, Guid? winnerAthleteId, FightResultMethod method, int? round, string? time);
    internal void Update(Fight fight, Guid? winnerAthleteId, FightResultMethod method, int? round, string? time);

    public (RecordDelta Red, RecordDelta Blue) GetDeltas(Guid redAthleteId, Guid blueAthleteId)
        => Method switch
        {
            FightResultMethod.NoContest => (RecordDelta.Zero, RecordDelta.Zero),
            FightResultMethod.Draw => (new(0, 0, 1, 0), new(0, 0, 1, 0)),
            _ => WinnerAthleteId == redAthleteId
                ? (WinnerDelta(), LoserDelta())
                : (LoserDelta(), WinnerDelta()),
        };
    // WinnerDelta: KO/TKO → (1,0,0,1); otherwise (1,0,0,0). LoserDelta: (0,1,0,0).
}
```

Validation (private static, used by ctor and Update): `HasWinner(method) = method is not Draw and not NoContest`; winner required ⇔ HasWinner, and must equal red or blue corner; round/time allowed only for Ko/Tko/Disqualification; round required for Ko/Tko; `round` ∈ 1..`fight.Rounds`; time matches `^\d{1,2}:[0-5]\d$`.

`Fight` additions (all `internal` except the nav): `Result` property; `RecordResult` (requires `Scheduled`), `ChangeResult`, `DeleteResult`, `Cancel`, `MarkNoContest`, `Reinstate` — each enforcing the status rules above and re-deriving `Status` from the method (`NoContest` method ⇒ `FightStatus.NoContest`, otherwise `Completed`).

`Event` additions (public, before private helpers): `RecordResult` (guards: `Status != Cancelled`, `DateOnly.FromDateTime(Date) <= today`), `ChangeResult`, `DeleteResult`, `CancelFight`, `MarkFightNoContest`, `ReinstateFight` — find the fight (`FindFight`), delegate, touch `UpdatedAt`. These do NOT call `EnsureCardEditable`.

- [ ] **Step 4: Run tests** — filter FightResultTests then whole domain suite; expected: PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add fight results with method effects to Event aggregate"`

---

### Task 3: Persistence — FightResults table and athlete counters

**Files:**
- Modify: `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`
- Create: `src/Sfc.Infrastructure/Migrations/*_AddFightResults.cs` (generated)
- Test: `tests/Sfc.Web.Tests/Persistence/FightResultPersistenceTests.cs`

**Interfaces:**
- Produces: `DbSet<FightResult> SfcDbContext.FightResults`; unique index on `FightId`; FK FightResult→Fight cascade; FK `WinnerAthleteId`→Athlete restrict; `Fight.Result` 1:1 nav; four int columns on Athletes (default 0).

- [ ] **Step 1: Failing test** — round-trip: create event+fight, `RecordResult`, `db.FightResults.Add(result)` (client-generated key, same reasoning as `AddFightAsync`), save, clear tracker, reload with `.Include(e => e.Fights).ThenInclude(f => f.Result)` and assert method/round/time and status; second test: severing (`DeleteResult`) deletes the orphan row on save; third: athlete result counters round-trip.
- [ ] **Step 2: Verify failure** — `dotnet test tests/Sfc.Web.Tests --filter FightResultPersistenceTests`; build error.
- [ ] **Step 3: Implement** — `DbSet<FightResult>`; config block:

```csharp
        builder.Entity<FightResult>(entity =>
        {
            entity.Property(r => r.Method).HasConversion<string>().HasMaxLength(30);
            entity.Property(r => r.Time).HasMaxLength(5);
            entity.HasIndex(r => r.FightId).IsUnique();
            entity.HasOne<Fight>().WithOne(f => f.Result)
                .HasForeignKey<FightResult>(r => r.FightId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Athlete>().WithMany()
                .HasForeignKey(r => r.WinnerAthleteId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 4: Migration** — `dotnet ef migrations add AddFightResults -p src/Sfc.Infrastructure -s src/Sfc.Infrastructure`; verify FightResults table + 4 Athlete columns; never edit applied migrations.
- [ ] **Step 5: Run tests; commit** — `git commit -m "Add FightResults persistence with AddFightResults migration"`

---

### Task 4: EventService result operations (transactional) and delete guards

**Files:**
- Modify: `src/Sfc.Web/Services/EventService.cs`
- Test: `tests/Sfc.Web.Tests/Services/EventResultServiceTests.cs`
- Modify: `tests/Sfc.Web.Tests/Services/EventServiceTests.cs` (delete-guard cases)

**Interfaces:**
- Produces:
  - `record ResultInput(Guid? WinnerAthleteId, FightResultMethod Method, int? Round, string? Time)`
  - `enum ResultOperationResult { Success, EventNotFound, FightNotFound, EventCancelled, EventNotYetHeld, FightNotScheduled, HasNoResult, HasResult, InvalidInput }`
  - `Task<ResultOperationResult> SaveResultAsync(Guid eventId, Guid fightId, ResultInput input, CancellationToken ct = default)` — records when the fight has no result, corrects otherwise (revert old deltas + apply new in ONE `SaveChanges`)
  - `Task<ResultOperationResult> DeleteResultAsync(Guid eventId, Guid fightId, CancellationToken ct = default)`
  - `Task<ResultOperationResult> CancelFightAsync/MarkFightNoContestAsync/ReinstateFightAsync(Guid eventId, Guid fightId, CancellationToken ct = default)`
  - `CardOperationResult.HasResult` — `RemoveFightAsync` returns it when the fight has a result
  - `EventDeleteResult.HasResults` — event delete blocked while any fight has a result
  - `GetWithCardAsync` additionally includes `Fight.Result`
  - Today (Europe/Lisbon): `TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon")` over `DateTime.UtcNow`.

- [ ] **Step 1: Failing tests** — happy path per representative method (KO updates both athletes incl. KO counter; draw updates both draws; NC changes nothing); correction KO→Draw reverts the KO win/loss and applies draws (assert final counters on BOTH athletes); correction swapping winner; delete reverts; future-dated event → `EventNotYetHeld`; cancelled event → `EventCancelled`; result on non-Scheduled fight → `FightNotScheduled`; `RemoveFightAsync` on fight with result → `HasResult`; event `DeleteAsync` with results → `HasResults`; cancel/NC/reinstate flows.
- [ ] **Step 2: Verify failure.**
- [ ] **Step 3: Implement** — load `Events.Include(e => e.Fights).ThenInclude(f => f.Result)`, find fight, load its two athletes tracked (`db.Athletes.Where(a => ids.Contains(a.Id))`), then:
  - record: pre-checks (cancelled/date/fight status) mirroring domain guards for friendly enums; `evt.RecordResult(...)`; `db.FightResults.Add(result)`; apply `GetDeltas` to red/blue athletes
  - correct: capture `oldDeltas = result.GetDeltas(...)` BEFORE `ChangeResult`; apply `old.Negate()` then new deltas
  - delete: apply negated deltas, `evt.DeleteResult(fightId)` (EF deletes the orphan)
  - single `SaveChangesAsync` per operation (one transaction); `ArgumentException` → `InvalidInput`
- [ ] **Step 4: Run the whole `Sfc.Web.Tests` suite.**
- [ ] **Step 5: Commit** — `git commit -m "Add transactional result operations to event service"`

---

### Task 5: UI — result entry, correction, and fight status actions

**Files:**
- Create: `src/Sfc.Web/Pages/Admin/Events/Fights/Result.cshtml` + `.cs` (route `{fightId:guid}`)
- Modify: `src/Sfc.Web/Pages/Admin/Events/Edit.cshtml` + `.cs`
- Modify: `src/Sfc.Web/Services/PtDisplay.cs` (method labels + result summary)

**Interfaces:**
- Consumes: Task 4 service methods; follows `Fights/Replace` page pattern for locating the fight/event.
- Produces: pt-PT labels — KO, TKO, Decisão unânime, Decisão dividida, Decisão por maioria, Empate, No contest, Desqualificação, Desistência; summary `"Vitória de {nome} por {método}"` (+ `" — R{round}"`, `" {tempo}"` when present), `"Empate"`, `"No contest"`.

- [ ] **Step 1: Result page** — mobile-first two-step flow in one page:
  - GET: fight header (names, order, billing) + current result summary when present; form: outcome picker as four large buttons (radio cards): Canto vermelho {nome} / Canto azul {nome} / Empate / No contest; method radios; round (`select` 1..Rounds) and time (`input inputmode="numeric" placeholder="m:ss"`) shown only for KO/TKO/DQ via minimal inline JS toggling on method change (no libs)
  - POST `OnPostReviewAsync`: server-side validation with pt-PT errors; renders confirmation summary with hidden fields + «Confirmar» / «Voltar»
  - POST `OnPostConfirmAsync`: `SaveResultAsync`; redirect to event Edit with TempData success «Resultado gravado.»; map every `ResultOperationResult` to a pt-PT message
  - POST `OnPostDeleteAsync` («Apagar resultado», `onsubmit return confirm(...)`): `DeleteResultAsync`
- [ ] **Step 2: Edit page card section** — per fight: result summary or status badge (Cancelado / No contest); buttons: «Resultado» (Scheduled + Completed/NoContest for correction), «Cancelar combate» / «No contest» (Scheduled, with confirm), «Reativar» (Cancelled/NoContest without result); three POST handlers calling the Task 4 service methods.
- [ ] **Step 3: Build + full test suite; fix warnings (TreatWarningsAsErrors).**
- [ ] **Step 4: Manual smoke test in browser (dev server + seeded data).**
- [ ] **Step 5: Commit** — `git commit -m "Add result entry and fight status actions to backoffice"`

---

### Task 6: Gates and PR

- [ ] Run `guardiao-ambito` on the diff; fix any scope findings.
- [ ] Run `revisor-dominio` with explicit attention to result-correction cases; fix findings.
- [ ] Run `/security-review`; fix findings.
- [ ] Full `dotnet test` green; push branch; open PR (base: `feature/eventos-fightcard` until PR #4 merges, GitHub retargets to `master` on merge).
