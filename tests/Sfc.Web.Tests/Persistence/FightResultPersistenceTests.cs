using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests.Persistence;

public class FightResultPersistenceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static readonly DateOnly Today = new(2026, 7, 18);

    [Fact]
    public async Task FightResult_RoundTripsWithFight()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var (evt, fight, red, _) = await SeedFightAsync(db, "result-roundtrip");

        var result = evt.RecordResult(fight.Id, red.Id, FightResultMethod.Tko, 2, "1:34", Today);
        db.FightResults.Add(result);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Fights
            .Include(f => f.Result)
            .SingleAsync(f => f.Id == fight.Id);

        Assert.Equal(FightStatus.Completed, loaded.Status);
        Assert.NotNull(loaded.Result);
        Assert.Equal(FightResultMethod.Tko, loaded.Result.Method);
        Assert.Equal(2, loaded.Result.Round);
        Assert.Equal("1:34", loaded.Result.Time);
        Assert.Equal(red.Id, loaded.Result.WinnerAthleteId);
    }

    [Fact]
    public async Task DeletingResultFromAggregate_RemovesOrphanRow()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var (evt, fight, red, _) = await SeedFightAsync(db, "result-orphan");

        var result = evt.RecordResult(fight.Id, red.Id, FightResultMethod.Ko, 1, null, Today);
        db.FightResults.Add(result);
        await db.SaveChangesAsync();

        evt.DeleteResult(fight.Id);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.FightResults.CountAsync(r => r.FightId == fight.Id));
    }

    [Fact]
    public async Task AthleteResultCounters_RoundTrip()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var athlete = NewAthlete(db, "counters-roundtrip");
        athlete.ApplyResultDelta(new RecordDelta(2, 1, 0, 1));
        db.Athletes.Add(athlete);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Athletes.SingleAsync(a => a.Id == athlete.Id);

        Assert.Equal(2, loaded.ResultWins);
        Assert.Equal(1, loaded.ResultLosses);
        Assert.Equal(1, loaded.ResultKos);
        Assert.Equal(2, loaded.Wins);
    }

    private async Task<(Event Event, Fight Fight, Athlete Red, Athlete Blue)> SeedFightAsync(
        SfcDbContext db, string slugPrefix)
    {
        var red = NewAthlete(db, $"{slugPrefix}-red");
        var blue = NewAthlete(db, $"{slugPrefix}-blue");
        db.Athletes.AddRange(red, blue);

        var evt = new Event(db.CurrentOrganizationId, $"SFC {slugPrefix}",
            new DateTime(2026, 7, 1, 20, 0, 0), slugPrefix);
        var fight = evt.AddFight(red.Id, blue.Id, Discipline.K1, 3, 3, "-72kg", null, false, false);
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return (evt, fight, red, blue);
    }

    private static Athlete NewAthlete(SfcDbContext db, string slug)
        => new(db.CurrentOrganizationId, "Atleta", slug, new DateOnly(1998, 3, 1), "Portugal",
            Discipline.K1, AthleteStatus.Amateur, slug);
}
