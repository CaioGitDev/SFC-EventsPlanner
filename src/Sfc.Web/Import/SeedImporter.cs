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

        var existing = (await db.Clubs.AsNoTracking().Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in CsvTable.Read(path))
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
                report.Error(ex.Message);
            }
            catch (ArgumentException ex)
            {
                report.Error(row.Fail(ex.Message).Message);
            }
        }
    }
}
