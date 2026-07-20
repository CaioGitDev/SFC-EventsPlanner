using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Xunit;

namespace Sfc.Web.Tests.Persistence;

public class EventFightPersistenceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Event_WithFights_RoundTripsOrderedWithAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = NewAthlete(db, "persist-red");
        var blue = NewAthlete(db, "persist-blue");
        db.Athletes.AddRange(red, blue);

        var evt = new Event(db.CurrentOrganizationId, "SFC Persist", new DateTime(2026, 10, 1, 20, 0, 0), "sfc-persist");
        evt.AddFight(red.Id, blue.Id, Discipline.K1, 3, 3, "-72kg", null, false, true);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.Events
            .Include(e => e.Fights).ThenInclude(f => f.RedCornerAthlete)
            .SingleAsync(e => e.Id == evt.Id);

        var fight = Assert.Single(loaded.Fights);
        Assert.Equal(FightBilling.Main, fight.Billing);
        Assert.Equal("persist-red", fight.RedCornerAthlete!.Slug);
        Assert.Equal(new DateTime(2026, 10, 1, 20, 0, 0), loaded.Date);
    }

    [Fact]
    public async Task RemovingFightFromAggregate_DeletesRow()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = NewAthlete(db, "orphan-red");
        var blue = NewAthlete(db, "orphan-blue");
        db.Athletes.AddRange(red, blue);
        var evt = new Event(db.CurrentOrganizationId, "SFC Orphan", new DateTime(2026, 10, 2, 20, 0, 0), "sfc-orphan");
        var fight = evt.AddFight(red.Id, blue.Id, Discipline.Boxing, 4, 2, null, 68.5m, false, true);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        evt.RemoveFight(fight.Id);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Fights.CountAsync(f => f.EventId == evt.Id));
    }

    [Fact]
    public async Task DeletingAthleteWithFight_ThrowsRestrict()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = NewAthlete(db, "restrict-red");
        var blue = NewAthlete(db, "restrict-blue");
        db.Athletes.AddRange(red, blue);
        var evt = new Event(db.CurrentOrganizationId, "SFC Restrict", new DateTime(2026, 10, 3, 20, 0, 0), "sfc-restrict");
        evt.AddFight(red.Id, blue.Id, Discipline.Mma, 3, 5, "-77kg", null, false, false);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        // Clear the tracker so the Fight isn't loaded alongside the athlete: with both
        // tracked in the same context, EF's own client-side check throws
        // InvalidOperationException before ever reaching the database. Reloading the
        // athlete alone forces the delete to hit Postgres, where the Restrict FK lives.
        db.ChangeTracker.Clear();
        var redToDelete = await db.Athletes.SingleAsync(a => a.Id == red.Id);
        db.Athletes.Remove(redToDelete);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Event_DuplicateSlugInSameOrganization_ThrowsOnSave()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        db.Events.Add(new Event(db.CurrentOrganizationId, "Dup A", new DateTime(2026, 10, 4), "dup-event-slug"));
        await db.SaveChangesAsync();
        db.Events.Add(new Event(db.CurrentOrganizationId, "Dup B", new DateTime(2026, 10, 5), "dup-event-slug"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Athlete NewAthlete(SfcDbContext db, string slug)
        => new(db.CurrentOrganizationId, "Atleta", slug, new DateOnly(1998, 3, 1), "Portugal",
            Discipline.K1, AthleteStatus.Amateur, slug);
}
