using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class ResultPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput NewAthlete(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    private async Task<(Guid EventId, Guid FightId, Guid RedId)> SeedAsync(
        IServiceProvider services, string name)
    {
        var events = services.GetRequiredService<EventService>();
        var athletes = services.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete(name, "Vermelho"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete(name, "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput(name, null,
            new DateTime(2026, 7, 1, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id,
            new FightInput(red.Id, blue.Id, Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;
        return (evt.Id, fightId, red.Id);
    }

    [Fact]
    public async Task ResultPage_ReviewShowsConfirmationStep()
    {
        using var scope = factory.Services.CreateScope();
        var (eventId, fightId, _) = await SeedAsync(scope.ServiceProvider, "Página Rever");

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var url = $"/Admin/Events/Fights/Result/{eventId}/{fightId}";
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, url);

        var response = await client.PostAsync($"{url}?handler=Review",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Outcome", "Red"),
                new KeyValuePair<string, string>("Method", "Ko"),
                new KeyValuePair<string, string>("Round", "2"),
                new KeyValuePair<string, string>("Time", "1:34"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Razor HTML-encodes non-ASCII, so assert on ASCII-safe fragments of the summary.
        Assert.Contains("Confirmar resultado", html);
        Assert.Contains("por KO", html);
        Assert.Contains("round 2, 1:34", html);
    }

    [Fact]
    public async Task ResultPage_ConfirmRecordsResultAndUpdatesRecords()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athleteService = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var (eventId, fightId, redId) = await SeedAsync(scope.ServiceProvider, "Página Confirmar");

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var url = $"/Admin/Events/Fights/Result/{eventId}/{fightId}";
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, url);

        var response = await client.PostAsync($"{url}?handler=Confirm",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Outcome", "Red"),
                new KeyValuePair<string, string>("Method", "Ko"),
                new KeyValuePair<string, string>("Round", "2"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var red = await athleteService.GetAsync(redId);
        Assert.Equal("1-0-0", red!.RecordDisplay);
        Assert.Equal(1, red.WinsByKo);
        var fight = (await events.GetWithCardAsync(eventId))!.Fights[0];
        Assert.NotNull(fight.Result);
    }

    [Fact]
    public async Task ResultPage_DeleteRevertsRecords()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athleteService = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var (eventId, fightId, redId) = await SeedAsync(scope.ServiceProvider, "Página Apagar");
        await events.SaveResultAsync(eventId, fightId,
            new ResultInput(redId, Sfc.Domain.Events.FightResultMethod.Ko, 1, null));

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var url = $"/Admin/Events/Fights/Result/{eventId}/{fightId}";
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, url);

        var response = await client.PostAsync($"{url}?handler=Delete",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Fresh scope: the seeding scope's DbContext still tracks the deleted result.
        using var verifyScope = factory.Services.CreateScope();
        var verifyEvents = verifyScope.ServiceProvider.GetRequiredService<EventService>();
        var verifyAthletes = verifyScope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await verifyAthletes.GetAsync(redId);
        Assert.Equal("0-0-0", red!.RecordDisplay);
        var fight = (await verifyEvents.GetWithCardAsync(eventId))!.Fights[0];
        Assert.Null(fight.Result);
    }
}
