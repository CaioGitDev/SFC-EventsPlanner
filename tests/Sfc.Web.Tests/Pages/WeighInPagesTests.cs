using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class WeighInPagesTests(SfcWebApplicationFactory factory)
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
            new DateTime(2026, 12, 15, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id,
            new FightInput(red.Id, blue.Id, Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;
        return (evt.Id, fightId, red.Id);
    }

    [Fact]
    public async Task WeighInsPage_PostSavesWeighInAndRedirects()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var (eventId, fightId, redId) = await SeedAsync(scope.ServiceProvider, "Página Pesagem");

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var url = $"/Admin/Events/WeighIns/{eventId}";
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, url);

        var response = await client.PostAsync(
            $"{url}?handler=Save&fightId={fightId}&athleteId={redId}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("OfficialWeightKg", "71.8"),
                new KeyValuePair<string, string>("IsApproved", "true"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var row = (await events.GetWeighInSummaryAsync(eventId)).Single(r => r.AthleteId == redId);
        Assert.Equal(71.8m, row.OfficialWeightKg);
        Assert.True(row.IsApproved);
        Assert.False(row.IsOverweight);
    }

    [Fact]
    public async Task WeighInsPage_OverweightAthlete_ShowsMissedWeightBadge()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var (eventId, fightId, redId) = await SeedAsync(scope.ServiceProvider, "Página Falha Peso");
        await events.SaveWeighInAsync(eventId, fightId, redId,
            new WeighInInput(74.3m, null, false, null)); // above the -72kg limit

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var html = await client.GetStringAsync($"/Admin/Events/WeighIns/{eventId}");

        Assert.Contains("Falhou o peso", html);
    }
}
