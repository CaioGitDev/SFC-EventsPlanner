using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests.Persistence;

public class WeighInPersistenceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task WeighIn_RoundTrips()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var (fight, red) = await SeedFightAsync(db, "weighin-roundtrip");

        var weighIn = new WeighIn(db.CurrentOrganizationId, fight.Id, red.Id, 72m);
        weighIn.RecordOfficialWeight(71.85m, new DateTime(2026, 9, 11, 18, 0, 0, DateTimeKind.Utc));
        weighIn.Approve();
        weighIn.SetNotes("OK à primeira");
        db.WeighIns.Add(weighIn);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.WeighIns.SingleAsync(w => w.Id == weighIn.Id);

        Assert.Equal(71.85m, loaded.OfficialWeightKg);
        Assert.Equal(72m, loaded.ExpectedWeightKg);
        Assert.True(loaded.IsApproved);
        Assert.Equal("OK à primeira", loaded.Notes);
        Assert.NotNull(loaded.WeighedAt);
    }

    [Fact]
    public async Task WeighIn_DuplicateAthleteInFight_ThrowsOnSave()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var (fight, red) = await SeedFightAsync(db, "weighin-duplicate");

        db.WeighIns.Add(new WeighIn(db.CurrentOrganizationId, fight.Id, red.Id, 72m));
        await db.SaveChangesAsync();
        db.WeighIns.Add(new WeighIn(db.CurrentOrganizationId, fight.Id, red.Id, 72m));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task<(Fight Fight, Athlete Red)> SeedFightAsync(SfcDbContext db, string slugPrefix)
    {
        var red = NewAthlete(db, $"{slugPrefix}-red");
        var blue = NewAthlete(db, $"{slugPrefix}-blue");
        db.Athletes.AddRange(red, blue);
        var evt = new Event(db.CurrentOrganizationId, $"SFC {slugPrefix}",
            new DateTime(2026, 9, 12, 20, 0, 0), slugPrefix);
        var fight = evt.AddFight(red.Id, blue.Id, Discipline.K1, 3, 3, "-72kg", null, false, false);
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return (fight, red);
    }

    private static Athlete NewAthlete(SfcDbContext db, string slug)
        => new(db.CurrentOrganizationId, "Atleta", slug, new DateOnly(1998, 3, 1), "Portugal",
            Discipline.K1, AthleteStatus.Amateur, slug);
}
