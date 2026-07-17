using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class FightPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput NewAthlete(string first, string last)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, null, null);

    [Fact]
    public async Task AddFight_PostsFormCreatesFightAndRedirectsToEdit()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete("Página", "Vermelha"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete("Página", "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Add Fight", null,
            new DateTime(2026, 12, 20, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client,
            $"/Admin/Events/Fights/Add/{evt.Id}");

        var response = await client.PostAsync($"/Admin/Events/Fights/Add/{evt.Id}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Form.RedCornerAthleteId", red.Id.ToString()),
                new KeyValuePair<string, string>("Form.BlueCornerAthleteId", blue.Id.ToString()),
                new KeyValuePair<string, string>("Form.Discipline", "MuayThai"),
                new KeyValuePair<string, string>("Form.Rounds", "3"),
                new KeyValuePair<string, string>("Form.RoundDurationMinutes", "3"),
                new KeyValuePair<string, string>("Form.WeightClass", "-72kg"),
                new KeyValuePair<string, string>("Form.IsTitleFight", "false"),
                new KeyValuePair<string, string>("Form.IsAmateur", "false"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var card = (await events.GetWithCardAsync(evt.Id))!.Fights;
        var fight = Assert.Single(card);
        Assert.Equal(red.Id, fight.RedCornerAthleteId);
    }

    [Fact]
    public async Task AddFight_WithBothWeightFields_ShowsValidationError()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete("XOR", "Vermelho"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete("XOR", "Azul"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento XOR", null,
            new DateTime(2026, 12, 21, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client,
            $"/Admin/Events/Fights/Add/{evt.Id}");

        var response = await client.PostAsync($"/Admin/Events/Fights/Add/{evt.Id}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Form.RedCornerAthleteId", red.Id.ToString()),
                new KeyValuePair<string, string>("Form.BlueCornerAthleteId", blue.Id.ToString()),
                new KeyValuePair<string, string>("Form.Discipline", "K1"),
                new KeyValuePair<string, string>("Form.Rounds", "3"),
                new KeyValuePair<string, string>("Form.RoundDurationMinutes", "3"),
                new KeyValuePair<string, string>("Form.WeightClass", "-72kg"),
                new KeyValuePair<string, string>("Form.CatchweightKg", "74.5"),
                // The number-input tag helper renders this companion field so browsers
                // always post it; without it, "74.5" fails to bind on machines whose
                // culture (e.g. pt-PT) does not use '.' as the decimal separator.
                new KeyValuePair<string, string>("__Invariant", "Form.CatchweightKg"),
                new KeyValuePair<string, string>("Form.IsTitleFight", "false"),
                new KeyValuePair<string, string>("Form.IsAmateur", "false"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("categoria de peso OU peso combinado", html);
        Assert.Empty((await events.GetWithCardAsync(evt.Id))!.Fights);
    }

    [Fact]
    public async Task Replace_PostsFormAndSwapsAthlete()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(NewAthlete("Substituir", "Vermelho"), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(NewAthlete("Substituir", "Azul"), (0, 0, 0, 0), null);
        var sub = await athletes.CreateAsync(NewAthlete("Substituto", "Novo"), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Substituição", null,
            new DateTime(2026, 12, 22, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, new FightInput(red.Id, blue.Id,
            Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client,
            $"/Admin/Events/Fights/Replace/{evt.Id}/{fightId}");

        var response = await client.PostAsync($"/Admin/Events/Fights/Replace/{evt.Id}/{fightId}",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("Corner", "Blue"),
                new KeyValuePair<string, string>("NewAthleteId", sub.Id.ToString()),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Verify through a fresh scope: the setup scope's DbContext still tracks the
        // pre-replacement Fight, and EF identity resolution would return that stale
        // instance instead of the row the server's own context updated.
        using var verifyScope = factory.Services.CreateScope();
        var verifyEvents = verifyScope.ServiceProvider.GetRequiredService<EventService>();
        var fight = (await verifyEvents.GetWithCardAsync(evt.Id))!.Fights[0];
        Assert.Equal(sub.Id, fight.BlueCornerAthleteId);
        Assert.Equal(red.Id, fight.RedCornerAthleteId);
    }
}
