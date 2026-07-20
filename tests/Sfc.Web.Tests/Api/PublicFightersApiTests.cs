using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Api;

public class PublicFightersApiTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static AthleteInput AthleteInput(string first, string last, bool consent)
        => new(first, last, consent ? "Máquina" : null, new DateOnly(1998, 5, 10), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            consent, null, null);

    [Fact]
    public async Task FighterProfile_CarriesAgeRecordAndHistory()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var fighter = await athletes.CreateAsync(AthleteInput("Perfil", "Completo", true), (10, 2, 0, 4), null);
        var pastOpponent = await athletes.CreateAsync(AthleteInput("Perfil", "Adversário", false), (0, 0, 0, 0), null);
        var nextOpponent = await athletes.CreateAsync(AthleteInput("Perfil", "Seguinte", true), (5, 5, 0, 0), null);

        // A completed past event with a KO win.
        var pastEvent = await events.CreateAsync(new EventInput("Perfil Gala Passada", null,
            new DateTime(2026, 7, 4, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(pastEvent.Id, new FightInput(fighter.Id, pastOpponent.Id,
            Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var pastFightId = (await events.GetWithCardAsync(pastEvent.Id))!.Fights[0].Id;
        await events.PublishAsync(pastEvent.Id);
        await events.SaveResultAsync(pastEvent.Id, pastFightId,
            new ResultInput(fighter.Id, FightResultMethod.Ko, 2, "1:11"));
        await events.CompleteAsync(pastEvent.Id);

        // A published future event with the next fight.
        var nextEvent = await events.CreateAsync(new EventInput("Perfil Gala Futura", null,
            DateTime.Today.AddDays(20).AddHours(20), null, null, null, null, null), null, null);
        await events.AddFightAsync(nextEvent.Id, new FightInput(fighter.Id, nextOpponent.Id,
            Discipline.MuayThai, 5, 3, null, 74.5m, true, false));
        await events.PublishAsync(nextEvent.Id);

        using var client = factory.CreateClient();
        var profile = await client.GetFromJsonAsync<JsonElement>(
            $"/api/public/fighters/{fighter.Slug}", Json);

        Assert.Equal("Perfil Completo", profile.GetProperty("name").GetString());
        Assert.Equal(28, profile.GetProperty("age").GetInt32());
        Assert.Equal("11-2-0", profile.GetProperty("record").GetString()); // baseline + KO win
        Assert.Equal(5, profile.GetProperty("winsByKo").GetInt32());
        Assert.False(profile.TryGetProperty("dateOfBirth", out _));

        var lastFight = profile.GetProperty("lastFights")[0];
        Assert.Equal("Perfil Gala Passada", lastFight.GetProperty("eventName").GetString());
        Assert.Equal("Perfil Adversário", lastFight.GetProperty("opponentName").GetString());
        Assert.Equal(JsonValueKind.Null, lastFight.GetProperty("opponentSlug").ValueKind); // no consent
        Assert.Contains("KO", lastFight.GetProperty("summary").GetString());

        var nextFight = profile.GetProperty("nextFight");
        Assert.Equal("Perfil Gala Futura", nextFight.GetProperty("eventName").GetString());
        Assert.Equal("Perfil Seguinte", nextFight.GetProperty("opponentName").GetString());
        Assert.Equal(nextOpponent.Slug, nextFight.GetProperty("opponentSlug").GetString());
    }

    [Fact]
    public async Task FighterProfile_WithoutConsent_Is404()
    {
        using var scope = factory.Services.CreateScope();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var privateAthlete = await athletes.CreateAsync(
            AthleteInput("Perfil", "Privado", false), (1, 0, 0, 0), null);

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/public/fighters/{privateAthlete.Slug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FighterProfile_UnknownSlug_Is404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/public/fighters/nao-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
