using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Import;
using Xunit;

namespace Sfc.Web.Tests.Import;

/// <summary>Guards the shipped mock dataset: it must stay uncomfortable enough that the
/// dress rehearsal exercises the RGPD, missing-photo and foreign-nationality paths.</summary>
public class SeedDatasetTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static string SeedDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "seed")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);
        return Path.Combine(dir, "data", "seed");
    }

    [Fact]
    public async Task ShippedDataset_ImportsWithoutErrorsAndMeetsInvariants()
    {
        using var scope = factory.Services.CreateScope();
        var importer = ActivatorUtilities.CreateInstance<SeedImporter>(scope.ServiceProvider);

        var report = await importer.ImportAsync(SeedDirectory(), dryRun: false);

        Assert.Empty(report.Errors);
        Assert.Equal(10, report.CountCreated("clubs.csv"));
        Assert.Equal(45, report.CountCreated("athletes.csv"));
        Assert.Equal(2, report.CountCreated("events.csv"));
        Assert.Equal(20, report.CountCreated("fights.csv"));
        Assert.Equal(20, report.CountCreated("results.csv"));

        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var athletes = await db.Athletes.AsNoTracking().ToListAsync();

        // Fixed cut-off, not Age: Age moves with the wall clock, so asserting on it would
        // quietly start failing once the 2009-2011 cohort turns 18.
        var minorCutoff = new DateOnly(2008, 1, 1);
        Assert.True(athletes.Count(a => a.DateOfBirth > minorCutoff && !a.PublicProfileConsent) >= 3,
            "o dataset tem de conter menores sem consentimento — é o que testa o caminho RGPD");
        Assert.True(athletes.Count(a => a.Nickname is null) >= 15);
        Assert.True(athletes.Count(a => a.Wins == 0 && a.Losses == 0 && a.Draws == 0) >= 5);
        Assert.True(athletes.Count(a => a.Nationality != "Portugal") >= 2);
        Assert.All(athletes, a => Assert.Null(a.PhotoUrl));

        var events = await db.Events.AsNoTracking().ToListAsync();
        Assert.All(events, e => Assert.Equal(EventStatus.Completed, e.Status));

        // Records were built by results, not typed in.
        Assert.Contains(athletes, a => a.ResultWins > 0);
        Assert.Contains(athletes, a => a.ResultLosses > 0);
        Assert.Contains(athletes, a => a.ResultKos > 0);
    }
}
