using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class WeighInServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput AthleteInput(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    private sealed record Fixture(EventService Events, SfcDbContext Db,
        Guid EventId, Guid FightId, Athlete Red, Athlete Blue);

    private async Task<Fixture> SeedAsync(IServiceProvider services, string name)
    {
        var events = services.GetRequiredService<EventService>();
        var athletes = services.GetRequiredService<AthleteService>();
        var db = services.GetRequiredService<SfcDbContext>();

        var red = await athletes.CreateAsync(AthleteInput(name, "Vermelho"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(AthleteInput(name, "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput(name, null,
            new DateTime(2026, 12, 12, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id,
            new FightInput(red.Id, blue.Id, Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;

        return new Fixture(events, db, evt.Id, fightId, red, blue);
    }

    [Fact]
    public async Task SaveWeighInAsync_FirstSave_CreatesWithDefaultedExpectedWeight()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Criar");

        var result = await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Red.Id,
            new WeighInInput(71.8m, null, false, null));

        Assert.Equal(WeighInOperationResult.Success, result);
        var weighIn = await fx.Db.WeighIns.AsNoTracking()
            .SingleAsync(w => w.FightId == fx.FightId && w.AthleteId == fx.Red.Id);
        Assert.Equal(71.8m, weighIn.OfficialWeightKg);
        Assert.Equal(72m, weighIn.ExpectedWeightKg); // defaulted from the fight's -72kg limit
        Assert.NotNull(weighIn.WeighedAt);
    }

    [Fact]
    public async Task SaveWeighInAsync_SecondSave_UpdatesSameRow()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Upsert");
        await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Red.Id,
            new WeighInInput(73.2m, null, false, null));

        var result = await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Red.Id,
            new WeighInInput(71.9m, null, true, "Segunda tentativa"));

        Assert.Equal(WeighInOperationResult.Success, result);
        var weighIn = Assert.Single(await fx.Db.WeighIns.AsNoTracking()
            .Where(w => w.FightId == fx.FightId && w.AthleteId == fx.Red.Id).ToListAsync());
        Assert.Equal(71.9m, weighIn.OfficialWeightKg);
        Assert.True(weighIn.IsApproved);
        Assert.Equal("Segunda tentativa", weighIn.Notes);
    }

    [Fact]
    public async Task SaveWeighInAsync_AthleteNotInFight_ReturnsAthleteNotInFight()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Intruso");
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var outsider = await athletes.CreateAsync(AthleteInput("Pesagem", "Intruso"), (0, 0, 0, 0), null);

        var result = await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, outsider.Id,
            new WeighInInput(70m, null, false, null));

        Assert.Equal(WeighInOperationResult.AthleteNotInFight, result);
    }

    [Fact]
    public async Task SaveWeighInAsync_CancelledEvent_ReturnsEventCancelled()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Cancelado");
        await fx.Events.CancelAsync(fx.EventId);

        var result = await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Red.Id,
            new WeighInInput(71m, null, false, null));

        Assert.Equal(WeighInOperationResult.EventCancelled, result);
    }

    [Fact]
    public async Task SaveWeighInAsync_ApprovalWithoutWeight_ReturnsApprovalRequiresWeight()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Sem Peso");

        var result = await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Red.Id,
            new WeighInInput(null, null, true, null));

        Assert.Equal(WeighInOperationResult.ApprovalRequiresWeight, result);
    }

    [Fact]
    public async Task GetWeighInSummaryAsync_ReturnsAllCardAthletesWithOverweightFlag()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Resumo");
        await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Red.Id,
            new WeighInInput(73.5m, null, false, null)); // over the -72kg limit

        var rows = await fx.Events.GetWeighInSummaryAsync(fx.EventId);

        Assert.Equal(2, rows.Count);
        var redRow = rows.Single(r => r.AthleteId == fx.Red.Id);
        var blueRow = rows.Single(r => r.AthleteId == fx.Blue.Id);
        Assert.Equal(Corner.Red, redRow.Corner);
        Assert.True(redRow.IsOverweight);
        Assert.Equal(72m, redRow.WeightLimitKg);
        Assert.Null(blueRow.OfficialWeightKg);
        Assert.False(blueRow.IsOverweight);
        Assert.Equal(rows[0].AthleteId, redRow.AthleteId); // red corner listed first
    }

    [Fact]
    public async Task GetWeighInSummaryAsync_CarriesFightStatus()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Estado Combate");
        await fx.Events.CancelFightAsync(fx.EventId, fx.FightId);

        var rows = await fx.Events.GetWeighInSummaryAsync(fx.EventId);

        Assert.All(rows, r => Assert.Equal(FightStatus.Cancelled, r.FightStatus));
    }

    [Fact]
    public async Task ReplaceAthleteAsync_RemovesReplacedAthletesWeighIn()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Pesagem Substituição");
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var substitute = await athletes.CreateAsync(AthleteInput("Pesagem", "Suplente"), (0, 0, 0, 0), null);
        await fx.Events.SaveWeighInAsync(fx.EventId, fx.FightId, fx.Blue.Id,
            new WeighInInput(71.5m, null, true, null));

        var result = await fx.Events.ReplaceAthleteAsync(fx.EventId, fx.FightId, Corner.Blue, substitute.Id);

        Assert.Equal(CardOperationResult.Success, result);
        Assert.Equal(0, await fx.Db.WeighIns.CountAsync(
            w => w.FightId == fx.FightId && w.AthleteId == fx.Blue.Id));
    }
}
