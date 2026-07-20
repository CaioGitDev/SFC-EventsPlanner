# Public API Implementation Plan (Prompt 05 — Parte A)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Anonymous read-only `/api/public/...` endpoints with explicit DTOs for the Next.js portal, GDPR rules tested first, plus the on-demand portal revalidation trigger, per `docs/plans/2026-07-20-api-publica-design.md`.

**Architecture:** Minimal APIs in `src/Sfc.Web/Api/PublicApi.cs` (`MapPublicApi()` extension), all queries in a `PublicContentService` (AsNoTracking, slug-keyed, consent redaction in one place). `PortalRevalidator` posts to the portal's revalidate URL (config-gated, failures logged only) and is invoked from the existing `EventService` mutation paths.

**Tech Stack:** .NET 10 minimal APIs, EF Core, xUnit + WebApplicationFactory + Testcontainers.

## Global Constraints

- NEVER in any public payload: `DateOfBirth` (age only), contact fields, internal GUIDs, Draft events, unapproved weigh-in weights (ADR-004 + prompt).
- Athletes without `PublicProfileConsent`: name only everywhere; `/fighters/{slug}` → 404.
- Slugs are the only public keys; fights identified by card order.
- Events visible when `Published`/`Completed`/`Cancelled`; next event = closest future `Published` (Europe/Lisbon today).
- Weigh-ins public only when `IsApproved`.
- Revalidation trigger never breaks the backoffice operation (fire-and-forget with logging).
- Code EN / UI pt-PT unchanged; JSON camelCase.
- Branch `feature/api-publica`.

---

### Task 1: PublicContentService + event endpoints (next, list, detail with card)

**Files:**
- Create: `src/Sfc.Web/Api/PublicApi.cs` (`MapPublicApi()`; wire in `Program.cs`)
- Create: `src/Sfc.Web/Services/PublicContentService.cs` (DTOs + queries; register in DI)
- Test: `tests/Sfc.Web.Tests/Api/PublicEventsApiTests.cs`

**Interfaces (produced):**
- DTOs per design doc: `PublicEventSummary`, `PublicEventDetail`, `PublicFightCardEntry`, `PublicCardAthlete` (factory `PublicCardAthlete.From(Athlete?)` applies consent redaction in ONE place).
- `Task<PublicEventSummary?> GetNextEventAsync(ct)`; `Task<PublicEventsList> GetEventsAsync(ct)` (`upcoming`/`past`); `Task<PublicEventDetail?> GetEventAsync(string slug, ct)` (null for missing/Draft).
- Routes: `GET /api/public/events/next` (204 when none), `GET /api/public/events`, `GET /api/public/events/{slug}` (404 when null). All `.AllowAnonymous()`.

- [ ] Step 1: failing integration tests — GDPR raw-JSON scan (no `dateOfBirth`, no GUID regex match, no `"email"`), Draft absent from list and 404 on detail, Cancelled visible with status, next-event pick + 204, card entry redaction for a non-consenting athlete (only `name` non-null), consenting athlete carries slug/photo/record/age.
- [ ] Step 2: RED. Step 3: implement. Step 4: suite green. Step 5: commit `Add public events API with consent redaction`.

### Task 2: Fighter profile endpoint

**Files:** modify `PublicContentService.cs`, `PublicApi.cs`; test `tests/Sfc.Web.Tests/Api/PublicFightersApiTests.cs`

**Interfaces:** `PublicFighterProfile` (+ `PublicFighterFightRow`, `PublicUpcomingFight`); `Task<PublicFighterProfile?> GetFighterAsync(string slug, ct)` — null when missing, inactive, or no consent; last 5 completed fights (event public), next scheduled fight in a future `Published` event. Route `GET /api/public/fighters/{slug}`.

- [ ] TDD: profile carries age (not DoB), record, KO count; last-fights summaries match recorded results; next fight present; no-consent slug → 404; opponent without consent appears name-only (no slug).
- [ ] Commit `Add public fighter profile endpoint`.

### Task 3: Results + weight results endpoints

**Files:** modify `PublicContentService.cs`, `PublicApi.cs`; test `tests/Sfc.Web.Tests/Api/PublicResultsApiTests.cs`

**Interfaces:** `PublicFightResultRow` (order, billing, red/blue `PublicCardAthlete`, status, `result` with `winnerCorner`/`method`/`round`/`time`), `PublicWeighInRow` (order, athleteName, athleteSlug?, corner, weightClass/catchweightKg, officialWeightKg, weighedAt, missedWeight). `GetEventResultsAsync(slug)`, `GetEventWeighInsAsync(slug)` — null for missing/Draft events. Routes `GET /api/public/events/{slug}/results` and `/weigh-ins`.

- [ ] TDD: result rows carry winner corner + method; fights without result appear without `result`; NC/cancelled status exposed; weigh-ins show ONLY approved entries; missedWeight flag; unapproved weight absent from raw JSON; Draft event → 404.
- [ ] Commit `Add public results and weight results endpoints`.

### Task 4: PortalRevalidator

**Files:**
- Create: `src/Sfc.Web/Services/PortalRevalidator.cs` (+ `PortalOptions`; DI + `HttpClient` registration)
- Modify: `src/Sfc.Web/Services/EventService.cs` (call after publish/unpublish/complete/cancel, result save/delete, weigh-in save)
- Test: `tests/Sfc.Web.Tests/Services/PortalRevalidatorTests.cs`

**Interfaces:** `PortalOptions { RevalidateUrl, RevalidateSecret }` (config section `Portal`); `Task PortalRevalidator.TriggerAsync(string reason, string? eventSlug, CancellationToken ct)` — no-op when unconfigured; POST JSON `{ reason, eventSlug }` with header `x-revalidate-secret`; catches/logs all failures.

- [ ] TDD with a fake `HttpMessageHandler`: configured → publish triggers POST with secret and slug; unconfigured → zero calls; handler throwing → operation still succeeds.
- [ ] Commit `Add portal revalidation trigger on public content changes`.

### Task 5: Gates and PR

- [ ] guardiao-ambito; revisor-dominio (public presentation of cards/results/weigh-ins); `/security-review` (GDPR redaction is the core risk).
- [ ] Full suite green; smoke test endpoints in browser; push, PR to master, CI green, merge.
