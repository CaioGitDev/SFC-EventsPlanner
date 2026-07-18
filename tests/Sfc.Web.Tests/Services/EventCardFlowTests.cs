using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class EventCardFlowTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput AthleteInput(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    private static FightInput Fight(Guid red, Guid blue)
        => new(red, blue, Discipline.MuayThai, 3, 3, "-72kg", null, false, false);

    [Fact]
    public async Task CardOperations_OnCancelledEvent_ReturnEventLocked()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var a1 = await athletes.CreateAsync(AthleteInput("Locked", "Um"), (0, 0, 0, 0), null);
        var a2 = await athletes.CreateAsync(AthleteInput("Locked", "Dois"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Trancado", null,
            new DateTime(2026, 12, 28, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, Fight(a1.Id, a2.Id));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;
        await events.CancelAsync(evt.Id);

        Assert.Equal(CardOperationResult.EventLocked,
            await events.AddFightAsync(evt.Id, Fight(a1.Id, a2.Id)));
        Assert.Equal(CardOperationResult.EventLocked,
            await events.RemoveFightAsync(evt.Id, fightId));
        Assert.Equal(CardOperationResult.EventLocked,
            await events.MoveFightAsync(evt.Id, fightId, MoveDirection.Up));
        Assert.Equal(CardOperationResult.EventLocked,
            await events.ReplaceAthleteAsync(evt.Id, fightId, Corner.Blue, a2.Id));
    }

    [Fact]
    public async Task FullCardFlow_AddMoveReplaceRemove()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();

        var a1 = await athletes.CreateAsync(AthleteInput("Card", "Um"), (0, 0, 0, 0), null);
        var a2 = await athletes.CreateAsync(AthleteInput("Card", "Dois"), (0, 0, 0, 0), null);
        var a3 = await athletes.CreateAsync(AthleteInput("Card", "Três"), (0, 0, 0, 0), null);
        var a4 = await athletes.CreateAsync(AthleteInput("Card", "Quatro"), (0, 0, 0, 0), null);
        var a5 = await athletes.CreateAsync(AthleteInput("Card", "Cinco"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(
            new EventInput("Fluxo do Card", null, new DateTime(2026, 12, 5, 20, 0, 0),
                null, null, null, null, null), null, null);

        Assert.Equal(CardOperationResult.Success, await events.AddFightAsync(evt.Id, Fight(a1.Id, a2.Id)));
        Assert.Equal(CardOperationResult.Success, await events.AddFightAsync(evt.Id, Fight(a3.Id, a4.Id)));

        var listed = await events.SearchAsync("Fluxo do Card", null);
        Assert.Equal(2, Assert.Single(listed).FightCount);

        var card = (await events.GetWithCardAsync(evt.Id))!.Fights;
        Assert.Equal(FightBilling.CoMain, card[0].Billing);
        Assert.Equal(FightBilling.Main, card[1].Billing);

        Assert.Equal(CardOperationResult.Success,
            await events.MoveFightAsync(evt.Id, card[0].Id, MoveDirection.Down));
        var afterMove = (await events.GetWithCardAsync(evt.Id))!.Fights;
        Assert.Equal(card[0].Id, afterMove[1].Id);
        Assert.Equal(FightBilling.Main, afterMove[1].Billing);

        Assert.Equal(CardOperationResult.Success,
            await events.ReplaceAthleteAsync(evt.Id, afterMove[1].Id, Corner.Blue, a5.Id));
        var afterReplace = (await events.GetWithCardAsync(evt.Id))!.Fights;
        Assert.Equal(a5.Id, afterReplace[1].BlueCornerAthleteId);

        Assert.Equal(CardOperationResult.Success,
            await events.RemoveFightAsync(evt.Id, afterReplace[0].Id));
        var final = (await events.GetWithCardAsync(evt.Id))!.Fights;
        var remaining = Assert.Single(final);
        Assert.Equal(1, remaining.Order);
        Assert.Equal(FightBilling.Main, remaining.Billing);
    }

    [Fact]
    public async Task AddFightAsync_AthleteAlreadyInCard_ReturnsFriendlyResult()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var a1 = await athletes.CreateAsync(AthleteInput("Dup", "Um"), (0, 0, 0, 0), null);
        var a2 = await athletes.CreateAsync(AthleteInput("Dup", "Dois"), (0, 0, 0, 0), null);
        var a3 = await athletes.CreateAsync(AthleteInput("Dup", "Três"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(
            new EventInput("Card Duplicado", null, new DateTime(2026, 12, 6, 20, 0, 0),
                null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, Fight(a1.Id, a2.Id));

        Assert.Equal(CardOperationResult.AthleteAlreadyInCard,
            await events.AddFightAsync(evt.Id, Fight(a1.Id, a3.Id)));
        Assert.Equal(CardOperationResult.SameAthleteBothCorners,
            await events.AddFightAsync(evt.Id, Fight(a3.Id, a3.Id)));
    }

    [Fact]
    public async Task CardOperations_UnknownIds_ReturnNotFoundResults()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(
            new EventInput("Sem Combates", null, new DateTime(2026, 12, 7, 20, 0, 0),
                null, null, null, null, null), null, null);

        Assert.Equal(CardOperationResult.EventNotFound,
            await events.RemoveFightAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(CardOperationResult.FightNotFound,
            await events.RemoveFightAsync(evt.Id, Guid.NewGuid()));
        Assert.Equal(CardOperationResult.FightNotFound,
            await events.MoveFightAsync(evt.Id, Guid.NewGuid(), MoveDirection.Up));
    }
}
