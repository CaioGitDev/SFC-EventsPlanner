using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;

namespace Sfc.Web.Import;

/// <summary>
/// One-off seed import from CSV. Goes through the services (not the DbContext) so slug
/// generation, domain validation and record aggregation are exercised for real — the
/// point is to prove the platform, not to fill a database.
/// </summary>
public class SeedImporter(ClubService clubs, AthleteService athletes, EventService events,
    SfcDbContext db)
{
    public async Task<ImportReport> ImportAsync(string directory, bool dryRun,
        CancellationToken ct = default)
    {
        var report = new ImportReport();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ImportClubsAsync(directory, report, ct);
        await ImportAthletesAsync(directory, report, ct);
        await ImportEventsAsync(directory, report, ct);

        if (dryRun)
            await transaction.RollbackAsync(ct);
        else
            await transaction.CommitAsync(ct);

        return report;
    }

    private async Task ImportClubsAsync(string directory, ImportReport report,
        CancellationToken ct)
    {
        const string file = "clubs.csv";
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            return;

        List<CsvRow> rows;
        try
        {
            rows = CsvTable.Read(path, "name", "city", "country", "contact_email",
                "contact_phone", "coaches");
        }
        catch (ImportException ex)
        {
            // A bad header is a file-level problem: no row can be trusted, but it must not
            // abort the whole run — it is recorded like any other import error.
            report.Error(ex.Message);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // CsvTable.Read is driven by CsvHelper, which throws its own exception types
            // (BadDataException and other CsvHelperException subclasses) on malformed
            // quoting — a realistic occurrence in a hand-edited spreadsheet. Same contract
            // as a bad header: the file cannot be trusted, but the run must not abort, so
            // it is recorded like any other import error, exception type kept so a real
            // programming bug is not disguised as a spreadsheet problem.
            report.Error($"{file}: não foi possível ler o ficheiro ({ex.GetType().Name}): {ex.Message}");
            return;
        }

        var existing = (await db.Clubs.AsNoTracking().Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            try
            {
                var name = row.Required("name");
                if (!existing.Add(name))
                {
                    report.Skipped(file);
                    continue;
                }

                await clubs.CreateAsync(new ClubInput(name, row.Text("city"), row.Text("country"),
                    row.Text("contact_email"), row.Text("contact_phone"),
                    row.Text("coaches")?.Replace("|", "\n")), logo: null, ct);

                report.Created(file);
            }
            catch (ImportException ex)
            {
                // Already pt-PT and already carries file/line/column — pass through as-is.
                report.Error(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Import never aborts on a bad row (see ImportReport). Any other exception —
                // a domain ArgumentException, a DbUpdateException, a genuine bug — is framed
                // in pt-PT for the human reading the report, with the exception type kept so
                // a real programming bug is not disguised as a spreadsheet problem.
                report.Error(row.Fail(
                    $"rejeitado pela validação do domínio ({ex.GetType().Name}): {ex.Message}").Message);

                // A failure that reached SaveChangesAsync (e.g. a column-length violation)
                // leaves the rejected entity tracked as Added: the next row's SaveChangesAsync
                // would try to re-insert it alongside the new one and fail again, wrongly
                // rejecting an otherwise-valid row. Clearing the tracker drops that dangling
                // entity; already-committed rows stay in the database (this only forgets local
                // tracking, it does not touch the transaction), so it is safe to call
                // unconditionally, including for the ArgumentException case that never reached
                // db.Clubs.Add in the first place.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task ImportAthletesAsync(string directory, ImportReport report,
        CancellationToken ct)
    {
        const string file = "athletes.csv";
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            return;

        List<CsvRow> rows;
        try
        {
            rows = CsvTable.Read(path, "first_name", "last_name", "nickname", "date_of_birth",
                "nationality", "club_name", "coach_name", "discipline", "weight_class",
                "weight_kg", "height_cm", "status", "public_profile_consent", "baseline_wins",
                "baseline_losses", "baseline_draws", "baseline_kos", "notes");
        }
        catch (ImportException ex)
        {
            // Same contract as clubs.csv: a bad header cannot be trusted, but the run
            // must not abort.
            report.Error(ex.Message);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same contract as clubs.csv: CsvHelper's own exception types on malformed
            // quoting are recorded like any other import error, exception type kept so a
            // real programming bug is not disguised as a spreadsheet problem.
            report.Error($"{file}: não foi possível ler o ficheiro ({ex.GetType().Name}): {ex.Message}");
            return;
        }

        var clubIds = await db.Clubs.AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);

        var existing = (await db.Athletes.AsNoTracking()
                .Select(a => new { a.FirstName, a.LastName, a.DateOfBirth })
                .ToListAsync(ct))
            .Select(a => NaturalKey(a.FirstName, a.LastName, a.DateOfBirth))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            try
            {
                var firstName = row.Required("first_name");
                var lastName = row.Required("last_name");
                var dateOfBirth = row.Date("date_of_birth")
                    ?? throw row.Fail("date_of_birth", "valor obrigatório em falta");

                if (!existing.Add(NaturalKey(firstName, lastName, dateOfBirth)))
                {
                    report.Skipped(file);
                    continue;
                }

                Guid? clubId = null;
                if (row.Text("club_name") is { } clubName)
                {
                    if (!clubIds.TryGetValue(clubName, out var id))
                        throw row.Fail("club_name", $"clube '{clubName}' não existe em clubs.csv");

                    clubId = id;
                }

                var input = new AthleteInput(firstName, lastName, row.Text("nickname"),
                    dateOfBirth, row.Required("nationality"),
                    row.Enum<Discipline>("discipline")
                        ?? throw row.Fail("discipline", "valor obrigatório em falta"),
                    row.Enum<AthleteStatus>("status")
                        ?? throw row.Fail("status", "valor obrigatório em falta"),
                    clubId, row.Text("coach_name"), row.Text("weight_class"),
                    row.Decimal("weight_kg"), row.Int("height_cm"),
                    row.Bool("public_profile_consent"), Slug: null, row.Text("notes"));

                var baseline = (
                    Wins: row.Int("baseline_wins") ?? 0,
                    Losses: row.Int("baseline_losses") ?? 0,
                    Draws: row.Int("baseline_draws") ?? 0,
                    Kos: row.Int("baseline_kos") ?? 0);

                await athletes.CreateAsync(input, baseline, photo: null, ct);
                report.Created(file);
            }
            catch (ImportException ex)
            {
                // Already pt-PT and already carries file/line/column — pass through as-is.
                report.Error(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Same contract as clubs.csv rows: any other exception (domain
                // ArgumentException, DbUpdateException, a genuine bug) is framed in pt-PT
                // for the human reading the report, exception type kept so a real
                // programming bug is not disguised as a spreadsheet problem.
                report.Error(row.Fail(
                    $"rejeitado pela validação do domínio ({ex.GetType().Name}): {ex.Message}").Message);

                // Same reasoning as ImportClubsAsync: a row that reaches SaveChangesAsync
                // and fails leaves the rejected entity tracked as Added, which would break
                // every subsequent valid row in this file. Clearing the tracker is safe
                // unconditionally — already-committed rows stay in the (still open)
                // transaction; this only forgets local tracking.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task ImportEventsAsync(string directory, ImportReport report,
        CancellationToken ct)
    {
        const string file = "events.csv";
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            return;

        List<CsvRow> rows;
        try
        {
            rows = CsvTable.Read(path, "name", "slug", "date", "venue", "city",
                "tickets_url", "stream_url", "description", "status");
        }
        catch (ImportException ex)
        {
            // Same contract as clubs.csv/athletes.csv: a bad header cannot be trusted, but
            // the run must not abort.
            report.Error(ex.Message);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same contract as clubs.csv/athletes.csv: CsvHelper's own exception types on
            // malformed quoting are recorded like any other import error, exception type
            // kept so a real programming bug is not disguised as a spreadsheet problem.
            report.Error($"{file}: não foi possível ler o ficheiro ({ex.GetType().Name}): {ex.Message}");
            return;
        }

        // fights.csv and results.csv are read once up front, independently of whether they
        // fail: a bad header in either is recorded like any other import error but must not
        // block events.csv from importing (the card/results for that event are simply empty).
        var fightRows = ReadRelatedTable(directory, "fights.csv", report, "event_slug", "order",
            "discipline", "rounds", "round_duration_minutes", "weight_class", "catchweight_kg",
            "is_title_fight", "is_amateur", "red_athlete_slug", "blue_athlete_slug");
        var resultRows = ReadRelatedTable(directory, "results.csv", report, "event_slug",
            "fight_order", "winner_slug", "method", "round", "time");

        // Read up front via AsNoTracking(): the row loop below calls db.ChangeTracker.Clear()
        // on error, which must not leave these dictionaries depending on tracked entities.
        var athleteIds = await db.Athletes.AsNoTracking()
            .ToDictionaryAsync(a => a.Slug, a => a.Id, StringComparer.OrdinalIgnoreCase, ct);
        var existingSlugs = (await db.Events.AsNoTracking().Select(e => e.Slug).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            try
            {
                var slug = row.Required("slug");
                if (!existingSlugs.Add(slug))
                {
                    report.Skipped(file);
                    continue;
                }

                // Event.Date is a naive local DateTime (column "timestamp without time zone" —
                // see docs/plans/2026-07-15-eventos-fightcard-design.md): AdjustToUniversal
                // normalises any offset in the CSV value first, so parsing is deterministic
                // regardless of the machine's local time zone, then SpecifyKind strips the
                // resulting Utc marker Npgsql would otherwise reject for that column type,
                // taking the literal date/time digits as the naive local value.
                var date = DateTime.SpecifyKind(
                    DateTime.Parse(row.Required("date"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    DateTimeKind.Unspecified);

                var evt = await events.CreateAsync(new EventInput(row.Required("name"),
                    row.Text("description"), date, row.Text("venue"), row.Text("city"),
                    row.Text("tickets_url"), row.Text("stream_url"), slug),
                    banner: null, poster: null, ct);

                report.Created(file);

                await AddFightsAsync(evt.Id, slug, fightRows, athleteIds, report, ct);

                var status = row.Enum<EventStatus>("status") ?? EventStatus.Draft;
                if (status is EventStatus.Published or EventStatus.Completed)
                    await events.PublishAsync(evt.Id, ct);

                await SaveResultsAsync(evt.Id, slug, resultRows, athleteIds, report, ct);

                if (status is EventStatus.Completed)
                    await events.CompleteAsync(evt.Id, ct);
            }
            catch (ImportException ex)
            {
                // Already pt-PT and already carries file/line/column — pass through as-is.
                report.Error(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Same contract as clubs.csv/athletes.csv rows: any other exception (domain
                // ArgumentException, DbUpdateException, a genuine bug) is framed in pt-PT
                // for the human reading the report, exception type kept so a real
                // programming bug is not disguised as a spreadsheet problem.
                report.Error(row.Fail(
                    $"rejeitado pela validação do domínio ({ex.GetType().Name}): {ex.Message}").Message);

                // Same reasoning as ImportClubsAsync/ImportAthletesAsync: a row that fails
                // leaves rejected entities tracked, which would break every subsequent row.
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Adds the event's fights in card order (the CSV's <c>order</c> column);
    /// <see cref="Event.AddFight"/> derives billing from position, so no billing input
    /// exists here.</summary>
    private async Task AddFightsAsync(Guid eventId, string eventSlug, List<CsvRow> fightRows,
        Dictionary<string, Guid> athleteIds, ImportReport report, CancellationToken ct)
    {
        const string file = "fights.csv";
        var rows = fightRows
            .Where(r => string.Equals(r.Text("event_slug"), eventSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Int("order") ?? 0);

        foreach (var row in rows)
        {
            try
            {
                var result = await events.AddFightAsync(eventId, new FightInput(
                    AthleteId(row, "red_athlete_slug", athleteIds),
                    AthleteId(row, "blue_athlete_slug", athleteIds),
                    row.Enum<Discipline>("discipline")
                        ?? throw row.Fail("discipline", "valor obrigatório em falta"),
                    row.Int("rounds") ?? 3, row.Int("round_duration_minutes") ?? 3,
                    row.Text("weight_class"), row.Decimal("catchweight_kg"),
                    row.Bool("is_title_fight"), row.Bool("is_amateur")), ct);

                if (result is not CardOperationResult.Success)
                    throw row.Fail($"não foi possível adicionar o combate ({result})");

                report.Created(file);
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Error(row.Fail(
                    $"rejeitado pela validação do domínio ({ex.GetType().Name}): {ex.Message}").Message);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Saves results only after the event has been published (required lifecycle:
    /// Draft → fights added → Published → results saved → Completed).</summary>
    private async Task SaveResultsAsync(Guid eventId, string eventSlug, List<CsvRow> resultRows,
        Dictionary<string, Guid> athleteIds, ImportReport report, CancellationToken ct)
    {
        const string file = "results.csv";
        var evt = await events.GetWithCardAsync(eventId, ct);
        if (evt is null)
            return;

        var byOrder = evt.Fights.ToDictionary(f => f.Order, f => f.Id);
        var rows = resultRows
            .Where(r => string.Equals(r.Text("event_slug"), eventSlug, StringComparison.OrdinalIgnoreCase));

        foreach (var row in rows)
        {
            try
            {
                var order = row.Int("fight_order")
                    ?? throw row.Fail("fight_order", "valor obrigatório em falta");
                if (!byOrder.TryGetValue(order, out var fightId))
                    throw row.Fail($"não existe combate com ordem {order} em '{eventSlug}'");

                Guid? winnerId = row.Text("winner_slug") is { } slug
                    ? AthleteId(row, "winner_slug", athleteIds)
                    : null;

                var result = await events.SaveResultAsync(eventId, fightId, new ResultInput(
                    winnerId,
                    row.Enum<FightResultMethod>("method")
                        ?? throw row.Fail("method", "valor obrigatório em falta"),
                    row.Int("round"), row.Text("time")), ct);

                if (result is not ResultOperationResult.Success)
                    throw row.Fail($"não foi possível gravar o resultado ({result})");

                report.Created(file);
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Error(row.Fail(
                    $"rejeitado pela validação do domínio ({ex.GetType().Name}): {ex.Message}").Message);
                db.ChangeTracker.Clear();
            }
        }
    }

    private static Guid AthleteId(CsvRow row, string column, Dictionary<string, Guid> athleteIds)
    {
        var slug = row.Required(column);
        return athleteIds.TryGetValue(slug, out var id)
            ? id
            : throw row.Fail(column, $"não existe atleta com o slug '{slug}'");
    }

    private static List<CsvRow> ReadRelatedTable(string directory, string file, ImportReport report,
        params string[] knownColumns)
    {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            return [];

        try
        {
            return CsvTable.Read(path, knownColumns);
        }
        catch (ImportException ex)
        {
            report.Error(ex.Message);
            return [];
        }
        catch (Exception ex)
        {
            report.Error($"{file}: não foi possível ler o ficheiro ({ex.GetType().Name}): {ex.Message}");
            return [];
        }
    }

    private static string NaturalKey(string firstName, string lastName, DateOnly dateOfBirth)
        => $"{firstName.Trim()}|{lastName.Trim()}|{dateOfBirth:yyyy-MM-dd}";
}
