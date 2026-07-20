using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class EventResultServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static readonly DateTime PastDate = new(2026, 7, 1, 20, 0, 0);
    private static readonly DateTime FutureDate = new(2030, 12, 1, 20, 0, 0);

    private static AthleteInput AthleteInput(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    private sealed record Fixture(EventService Events, SfcDbContext Db,
        Guid EventId, Guid FightId, Athlete Red, Athlete Blue);

    private async Task<Fixture> SeedAsync(IServiceProvider services, string name,
        DateTime? date = null, (int W, int L, int D, int K)? redBaseline = null)
    {
        var events = services.GetRequiredService<EventService>();
        var athletes = services.GetRequiredService<AthleteService>();
        var db = services.GetRequiredService<SfcDbContext>();

        var red = await athletes.CreateAsync(AthleteInput(name, "Vermelho"), redBaseline ?? (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(AthleteInput(name, "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput(name, null, date ?? PastDate,
            null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id,
            new FightInput(red.Id, blue.Id, Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;

        return new Fixture(events, db, evt.Id, fightId, red, blue);
    }

    private static async Task<Athlete> ReloadAsync(SfcDbContext db, Guid id)
        => await db.Athletes.AsNoTracking().SingleAsync(a => a.Id == id);

    [Fact]
    public async Task SaveResultAsync_Ko_UpdatesBothAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Resultado KO", redBaseline: (5, 1, 0, 2));

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 2, "1:34"));

        Assert.Equal(ResultOperationResult.Success, result);
        var red = await ReloadAsync(fx.Db, fx.Red.Id);
        var blue = await ReloadAsync(fx.Db, fx.Blue.Id);
        Assert.Equal(6, red.Wins);
        Assert.Equal(3, red.WinsByKo);
        Assert.Equal(1, blue.Losses);
        Assert.Equal(0, blue.Wins);
    }

    [Fact]
    public async Task SaveResultAsync_Draw_UpdatesBothDraws()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Resultado Empate");

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(null, FightResultMethod.Draw, null, null));

        Assert.Equal(ResultOperationResult.Success, result);
        Assert.Equal(1, (await ReloadAsync(fx.Db, fx.Red.Id)).Draws);
        Assert.Equal(1, (await ReloadAsync(fx.Db, fx.Blue.Id)).Draws);
    }

    [Fact]
    public async Task SaveResultAsync_NoContest_ChangesNoRecords()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Resultado NC", redBaseline: (5, 1, 0, 2));

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(null, FightResultMethod.NoContest, null, null));

        Assert.Equal(ResultOperationResult.Success, result);
        var red = await ReloadAsync(fx.Db, fx.Red.Id);
        Assert.Equal("5-1-0", red.RecordDisplay);
        Assert.Equal(FightStatus.NoContest,
            (await fx.Events.GetWithCardAsync(fx.EventId))!.Fights[0].Status);
    }

    [Fact]
    public async Task SaveResultAsync_CorrectionKoToDraw_RevertsAndApplies()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Correção KO Empate", redBaseline: (5, 1, 0, 2));
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 2, null));

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(null, FightResultMethod.Draw, null, null));

        Assert.Equal(ResultOperationResult.Success, result);
        var red = await ReloadAsync(fx.Db, fx.Red.Id);
        var blue = await ReloadAsync(fx.Db, fx.Blue.Id);
        Assert.Equal(5, red.Wins);
        Assert.Equal(2, red.WinsByKo);
        Assert.Equal(1, red.Draws);
        Assert.Equal(0, blue.Losses);
        Assert.Equal(1, blue.Draws);
    }

    [Fact]
    public async Task SaveResultAsync_CorrectionSwapsWinner()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Correção Vencedor");
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Blue.Id, FightResultMethod.Tko, 3, "2:10"));

        Assert.Equal(ResultOperationResult.Success, result);
        var red = await ReloadAsync(fx.Db, fx.Red.Id);
        var blue = await ReloadAsync(fx.Db, fx.Blue.Id);
        Assert.Equal("0-1-0", red.RecordDisplay);
        Assert.Equal(0, red.WinsByKo);
        Assert.Equal("1-0-0", blue.RecordDisplay);
        Assert.Equal(1, blue.WinsByKo);
    }

    [Fact]
    public async Task DeleteResultAsync_RevertsRecordsAndRestoresScheduled()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Apagar Resultado");
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        var result = await fx.Events.DeleteResultAsync(fx.EventId, fx.FightId);

        Assert.Equal(ResultOperationResult.Success, result);
        Assert.Equal("0-0-0", (await ReloadAsync(fx.Db, fx.Red.Id)).RecordDisplay);
        Assert.Equal("0-0-0", (await ReloadAsync(fx.Db, fx.Blue.Id)).RecordDisplay);
        var fight = (await fx.Events.GetWithCardAsync(fx.EventId))!.Fights[0];
        Assert.Equal(FightStatus.Scheduled, fight.Status);
        Assert.Null(fight.Result);
    }

    [Fact]
    public async Task SaveResultAsync_FutureEvent_ReturnsEventNotYetHeld()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Evento Futuro", FutureDate);

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        Assert.Equal(ResultOperationResult.EventNotYetHeld, result);
    }

    [Fact]
    public async Task SaveResultAsync_CancelledEvent_ReturnsEventCancelled()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Evento Cancelado Resultado");
        await fx.Events.CancelAsync(fx.EventId);

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        Assert.Equal(ResultOperationResult.EventCancelled, result);
    }

    [Fact]
    public async Task SaveResultAsync_OnCancelledFight_ReturnsFightNotScheduled()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Combate Cancelado Resultado");
        await fx.Events.CancelFightAsync(fx.EventId, fx.FightId);

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        Assert.Equal(ResultOperationResult.FightNotScheduled, result);
    }

    [Fact]
    public async Task SaveResultAsync_KoWithoutRound_ReturnsInvalidInput()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "KO Sem Round");

        var result = await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, null, null));

        Assert.Equal(ResultOperationResult.InvalidInput, result);
    }

    [Fact]
    public async Task RemoveFightAsync_WithResult_ReturnsHasResult()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Remover Com Resultado");
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        Assert.Equal(CardOperationResult.HasResult,
            await fx.Events.RemoveFightAsync(fx.EventId, fx.FightId));
    }

    [Fact]
    public async Task DeleteAsync_EventWithResults_ReturnsHasResults()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Apagar Evento Com Resultados");
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));
        await fx.Events.CancelAsync(fx.EventId);

        Assert.Equal(EventDeleteResult.HasResults, await fx.Events.DeleteAsync(fx.EventId));
    }

    [Fact]
    public async Task CancelAndReinstateFight_FlowsThroughStatuses()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Cancelar Reativar");

        Assert.Equal(ResultOperationResult.Success,
            await fx.Events.CancelFightAsync(fx.EventId, fx.FightId));
        Assert.Equal(FightStatus.Cancelled,
            (await fx.Events.GetWithCardAsync(fx.EventId))!.Fights[0].Status);

        Assert.Equal(ResultOperationResult.Success,
            await fx.Events.ReinstateFightAsync(fx.EventId, fx.FightId));
        Assert.Equal(FightStatus.Scheduled,
            (await fx.Events.GetWithCardAsync(fx.EventId))!.Fights[0].Status);

        Assert.Equal(ResultOperationResult.Success,
            await fx.Events.MarkFightNoContestAsync(fx.EventId, fx.FightId));
        Assert.Equal(FightStatus.NoContest,
            (await fx.Events.GetWithCardAsync(fx.EventId))!.Fights[0].Status);
    }

    [Fact]
    public async Task CancelAsync_EventWithRecordedResults_ReturnsHasResults()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Cancelar Com Resultados");
        await fx.Events.PublishAsync(fx.EventId);
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));

        Assert.Equal(EventTransitionResult.HasResults, await fx.Events.CancelAsync(fx.EventId));

        await fx.Events.DeleteResultAsync(fx.EventId, fx.FightId);
        Assert.Equal(EventTransitionResult.Success, await fx.Events.CancelAsync(fx.EventId));
    }

    [Fact]
    public async Task CancelAsync_CompletedEventWithResults_ReturnsInvalidTransitionNotHasResults()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Cancelar Concluído");
        await fx.Events.PublishAsync(fx.EventId);
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 1, null));
        await fx.Events.CompleteAsync(fx.EventId);

        // A completed event can never be cancelled — reporting HasResults here would
        // send the operator off deleting results for nothing.
        Assert.Equal(EventTransitionResult.InvalidTransition, await fx.Events.CancelAsync(fx.EventId));
    }

    [Fact]
    public async Task AthleteListings_ShowRecordWithResultAggregation()
    {
        using var scope = factory.Services.CreateScope();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var fx = await SeedAsync(scope.ServiceProvider, "Listagem Agregada", redBaseline: (5, 1, 0, 2));
        await fx.Events.SaveResultAsync(fx.EventId, fx.FightId,
            new ResultInput(fx.Red.Id, FightResultMethod.Ko, 2, null));

        var searched = await athletes.SearchAsync("Listagem Agregada Vermelho", null, null);
        var listItem = Assert.Single(searched.Items);
        Assert.Equal("6-1-0", listItem.Record);

        var options = await athletes.ListActiveOptionsAsync("Listagem Agregada Vermelho", null, null);
        var option = Assert.Single(options);
        Assert.Contains("6-1-0", option.Label);
    }

    [Fact]
    public async Task ResultOperations_UnknownIds_ReturnNotFound()
    {
        using var scope = factory.Services.CreateScope();
        var fx = await SeedAsync(scope.ServiceProvider, "Resultado Ids Desconhecidos");

        Assert.Equal(ResultOperationResult.EventNotFound,
            await fx.Events.SaveResultAsync(Guid.NewGuid(), fx.FightId,
                new ResultInput(null, FightResultMethod.Draw, null, null)));
        Assert.Equal(ResultOperationResult.FightNotFound,
            await fx.Events.SaveResultAsync(fx.EventId, Guid.NewGuid(),
                new ResultInput(null, FightResultMethod.Draw, null, null)));
        Assert.Equal(ResultOperationResult.HasNoResult,
            await fx.Events.DeleteResultAsync(fx.EventId, fx.FightId));
    }
}
