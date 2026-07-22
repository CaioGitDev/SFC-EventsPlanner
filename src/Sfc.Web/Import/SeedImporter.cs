using Microsoft.EntityFrameworkCore;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;

namespace Sfc.Web.Import;

/// <summary>
/// One-off seed import from CSV. Goes through the services (not the DbContext) so slug
/// generation, domain validation and record aggregation are exercised for real — the
/// point is to prove the platform, not to fill a database.
/// </summary>
#pragma warning disable CS9113 // athletes/events are unread here by design: seams for Tasks 3 and 4.
public class SeedImporter(ClubService clubs, AthleteService athletes, EventService events,
    SfcDbContext db)
#pragma warning restore CS9113
{
    public async Task<ImportReport> ImportAsync(string directory, bool dryRun,
        CancellationToken ct = default)
    {
        var report = new ImportReport();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ImportClubsAsync(directory, report, ct);

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
}
