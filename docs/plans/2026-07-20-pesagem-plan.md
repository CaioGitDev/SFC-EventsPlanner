# Weigh-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simple weigh-in per card athlete (expected/official weight, time, approved, notes) with a mobile-first per-event entry view; weight misses flagged visually but never blocking, per `docs/plans/2026-07-20-pesagem-design.md`.

**Architecture:** `WeighIn` is a standalone entity (FightId + AthleteId, unique) upserted through `EventService` — it never mutates fights/events/records, so it stays outside the `Event` aggregate; fight-context invariants (athlete ∈ corners, event not cancelled) are service checks, mirroring the result-operation patterns. `Fight.WeightLimitKg` (domain, parsed from WeightClass/catchweight) powers overweight detection.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + Testcontainers PostgreSQL, Razor Pages.

## Global Constraints

- Code/entities/tests in **English**; UI strings in **pt-PT** (CLAUDE.md rule 4).
- Overweight NEVER blocks saving or approving (prompt rule — the call is human: catchweight or cancellation).
- One weigh-in per athlete per fight: unique index `(FightId, AthleteId)` + upsert semantics.
- `IsApproved` requires `OfficialWeightKg`; `WeighedAt` (UTC) set when official weight is recorded.
- Weigh-ins allowed on any event status except `Cancelled`.
- `OrganizationId` + query filter convention (ADR-002) — `WeighIn : IOrganizationScoped`.
- Branch `feature/pesagem`; migrations via `-p src/Sfc.Infrastructure -s src/Sfc.Infrastructure`.

---

### Task 1: Fight.WeightLimitKg + WeighIn entity (domain)

**Files:**
- Modify: `src/Sfc.Domain/Events/Fight.cs`
- Create: `src/Sfc.Domain/Events/WeighIn.cs`
- Test: `tests/Sfc.Domain.Tests/Events/WeighInTests.cs`

**Interfaces:**
- Produces: `decimal? Fight.WeightLimitKg` (catchweight wins; else first number parsed from `WeightClass`, `,` or `.` decimal separator; null when unparseable); `WeighIn` with public ctor `(Guid organizationId, Guid fightId, Guid athleteId, decimal? expectedWeightKg)`, `RecordOfficialWeight(decimal kg, DateTime weighedAtUtc)`, `Approve()` (throws `InvalidOperationException` without official weight), `Unapprove()`, `SetExpectedWeight(decimal?)`, `SetNotes(string?)`, `bool IsOverweight(decimal? limitKg)`.

- [ ] Step 1: failing tests — limit parsing (`74.5m` catchweight → 74.5; `"-72kg"` → 72; `"57,15 kg"` → 57.15; `"Peso Galo"` → null); ctor rejects non-positive expected weight; `RecordOfficialWeight` sets weight+`WeighedAt`, rejects ≤ 0; `Approve` without weight throws; approve when overweight succeeds; `Unapprove`; `IsOverweight` matrix (above/equal/below/null weight/null limit).
- [ ] Step 2: run `dotnet test tests/Sfc.Domain.Tests --filter WeighInTests` — build error expected.
- [ ] Step 3: implement (regex `[0-9]+([.,][0-9]+)?` on WeightClass for the limit; `NullIfBlank` for notes).
- [ ] Step 4: run full domain suite — PASS.
- [ ] Step 5: commit `Add WeighIn entity and fight weight limit parsing`.

### Task 2: Persistence — WeighIns table

**Files:**
- Modify: `src/Sfc.Infrastructure/Persistence/SfcDbContext.cs`
- Create: migration `AddWeighIns`
- Test: `tests/Sfc.Web.Tests/Persistence/WeighInPersistenceTests.cs`

**Interfaces:** `DbSet<WeighIn> SfcDbContext.WeighIns`; unique index `(FightId, AthleteId)`; FK→Fight cascade, FK→Athlete restrict; weights `HasPrecision(5, 2)`; `Notes` max 1000.

- [ ] Step 1: failing tests — round-trip; duplicate `(FightId, AthleteId)` insert → `DbUpdateException`.
- [ ] Step 2: verify failure; Step 3: DbSet + config + migration; Step 4: PASS; Step 5: commit `Add WeighIns persistence with AddWeighIns migration`.

### Task 3: EventService weigh-in operations

**Files:**
- Modify: `src/Sfc.Web/Services/EventService.cs`
- Test: `tests/Sfc.Web.Tests/Services/WeighInServiceTests.cs`

**Interfaces:**
- `record WeighInInput(decimal? OfficialWeightKg, decimal? ExpectedWeightKg, bool IsApproved, string? Notes)`
- `enum WeighInOperationResult { Success, EventNotFound, FightNotFound, AthleteNotInFight, EventCancelled, ApprovalRequiresWeight, InvalidInput }`
- `Task<WeighInOperationResult> SaveWeighInAsync(Guid eventId, Guid fightId, Guid athleteId, WeighInInput input, CancellationToken ct = default)` — upsert; expected weight defaults to `Fight.WeightLimitKg` on first save; `WeighedAt` = UTC now when official weight set/changed
- `record WeighInRow(Guid FightId, int FightOrder, Guid AthleteId, string AthleteName, Corner Corner, string? WeightClass, decimal? CatchweightKg, decimal? WeightLimitKg, decimal? ExpectedWeightKg, decimal? OfficialWeightKg, DateTime? WeighedAt, bool IsApproved, bool IsOverweight, string? Notes)`
- `Task<List<WeighInRow>> GetWeighInSummaryAsync(Guid eventId, CancellationToken ct = default)` — every card athlete (with or without weigh-in), ordered by fight order then corner (red first)
- `ReplaceAthleteAsync` additionally deletes the replaced athlete's weigh-in for that fight in the same SaveChanges.

- [ ] Step 1: failing tests — save creates row with defaulted expected weight; second save updates (upsert, official weight change refreshes `WeighedAt`); athlete not in fight → `AthleteNotInFight`; cancelled event → `EventCancelled`; `IsApproved` without weight → `ApprovalRequiresWeight`; summary returns all card athletes ordered with `IsOverweight` flag; replacing an athlete removes their weigh-in.
- [ ] Steps 2–4: RED → implement → full suite PASS.
- [ ] Step 5: commit `Add weigh-in operations to event service`.

### Task 4: Weigh-in view

**Files:**
- Create: `src/Sfc.Web/Pages/Admin/Events/WeighIns.cshtml` + `.cs` (route `{eventId:guid}`)
- Modify: `src/Sfc.Web/Pages/Admin/Events/Edit.cshtml` (link «Pesagem» next to «Adicionar combate»)

- [ ] Grid grouped by fight (order + names + weight class/limit); one row per athlete: expected weight (small input), official weight (`inputmode="decimal"`, step 0.05), approve checkbox, notes input, per-row «Gravar» POST; badges «Falhou o peso» / «Aprovado»; pt-PT messages for every `WeighInOperationResult`; double-submit guard script (same as Result page).
- [ ] Page test `tests/Sfc.Web.Tests/Pages/WeighInPagesTests.cs`: POST saves weigh-in and redirects; overweight athlete's page shows «Falhou o peso».
- [ ] Full suite green; smoke test in browser (mobile viewport); commit `Add per-event weigh-in view`.

### Task 5: Gates and PR

- [ ] guardiao-ambito + revisor-dominio (fight-sports reality of the weigh-in flow) + `/security-review`; fix findings.
- [ ] Push, PR to master, CI green, merge.
