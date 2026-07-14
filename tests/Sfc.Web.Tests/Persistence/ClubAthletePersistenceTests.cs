using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Clubs;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests.Persistence;

public class ClubAthletePersistenceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Club_WithCoaches_RoundTrips()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var club = new Club(db.CurrentOrganizationId, "Team Scorpion", "Lisboa", "Portugal");
        club.SetCoaches([new Coach("Mestre Rui", "rui@scorpion.pt"), new Coach("Kru Ana")]);
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Clubs.SingleAsync(c => c.Id == club.Id);

        Assert.Equal("Team Scorpion", loaded.Name);
        Assert.Equal(2, loaded.Coaches.Count);
        Assert.Equal("Mestre Rui", loaded.Coaches[0].Name);
        Assert.Null(loaded.Coaches[1].Contact);
    }

    [Fact]
    public async Task Athlete_WithClub_RoundTripsAndLoadsNavigation()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var club = new Club(db.CurrentOrganizationId, "Fight Lab");
        db.Clubs.Add(club);
        var athlete = new Athlete(db.CurrentOrganizationId, "João", "Peixão",
            new DateOnly(2000, 5, 20), "Portugal", Discipline.MuayThai,
            AthleteStatus.Professional, "joao-peixao-persistence",
            clubId: club.Id, baselineWins: 5, baselineKos: 2);
        db.Athletes.Add(athlete);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Athletes.Include(a => a.Club).SingleAsync(a => a.Id == athlete.Id);

        Assert.Equal("5-0-0", loaded.RecordDisplay);
        Assert.Equal("Fight Lab", loaded.Club!.Name);
        Assert.Equal(Discipline.MuayThai, loaded.Discipline);
    }

    [Fact]
    public async Task Athlete_DuplicateSlugInSameOrganization_ThrowsOnSave()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        db.Athletes.Add(NewAthlete(db, "duplicate-slug-test"));
        await db.SaveChangesAsync();
        db.Athletes.Add(NewAthlete(db, "duplicate-slug-test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Athlete NewAthlete(SfcDbContext db, string slug)
        => new(db.CurrentOrganizationId, "Ana", "Silva", new DateOnly(1998, 3, 1), "Portugal",
            Discipline.K1, AthleteStatus.Amateur, slug);
}
