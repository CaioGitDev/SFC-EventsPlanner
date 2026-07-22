using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Athletes;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;

namespace Sfc.Web.Import;

/// <summary>
/// One-off seed import from CSV. Goes through the services (not the DbContext) so slug
/// generation, domain validation and record aggregation are exercised for real — the
/// point is to prove the platform, not to fill a database.
/// </summary>
public class SeedImporter(ClubService clubs, AthleteService athletes,
#pragma warning disable CS9113 // events is unread here by design: seam for Task 4.
    EventService events,
#pragma warning restore CS9113
    SfcDbContext db)
{
    public async Task<ImportReport> ImportAsync(string directory, bool dryRun,
        CancellationToken ct = default)
    {
        var report = new ImportReport();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ImportClubsAsync(directory, report, ct);
        await ImportAthletesAsync(directory, report, ct);

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

    private static string NaturalKey(string firstName, string lastName, DateOnly dateOfBirth)
        => $"{firstName.Trim()}|{lastName.Trim()}|{dateOfBirth:yyyy-MM-dd}";
}
