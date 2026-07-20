using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Api;

public class PublicResultsApiTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static AthleteInput AthleteInput(string first, string last, bool consent)
        => new(first, last, null, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            consent, null, null);

    private sealed record Seeded(string EventSlug, Guid EventId, Guid FightId,
        Athlete Red, Athlete Blue, EventService Events);

    private async Task<Seeded> SeedPastEventAsync(IServiceProvider services, string name)
    {
        var events = services.GetRequiredService<EventService>();
        var athletes = services.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(AthleteInput(name, "Vermelho", true), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(AthleteInput(name, "Azul", false), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput(name, null,
            new DateTime(2026, 7, 3, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, new FightInput(red.Id, blue.Id,
            Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        await events.PublishAsync(evt.Id);
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;
        return new Seeded(evt.Slug, evt.Id, fightId, red, blue, events);
    }

    [Fact]
    public async Task Results_CarryWinnerCornerAndMethod()
    {
        using var scope = factory.Services.CreateScope();
        var s = await SeedPastEventAsync(scope.ServiceProvider, "Resultados Públicos");
        await s.Events.SaveResultAsync(s.EventId, s.FightId,
            new ResultInput(s.Blue.Id, FightResultMethod.Tko, 2, "0:58"));

        using var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/public/events/{s.EventSlug}/results", Json);

        var row = rows[0];
        // The results page must stand alone — fight context comes with the row.
        Assert.Equal("-72kg", row.GetProperty("weightClass").GetString());
        Assert.Equal("MuayThai", row.GetProperty("discipline").GetString());
        Assert.False(row.GetProperty("isTitleFight").GetBoolean());
        var result = row.GetProperty("result");
        Assert.Equal("blue", result.GetProperty("winnerCorner").GetString());
        Assert.Equal("Tko", result.GetProperty("method").GetString());
        Assert.Equal(2, result.GetProperty("round").GetInt32());
        Assert.Equal("0:58", result.GetProperty("time").GetString());
        // Redaction still applies on result rows.
        Assert.Equal(JsonValueKind.Null, row.GetProperty("blue").GetProperty("slug").ValueKind);
    }

    [Fact]
    public async Task Results_FightWithoutResult_HasNullResult()
    {
        using var scope = factory.Services.CreateScope();
        var s = await SeedPastEventAsync(scope.ServiceProvider, "Resultados Pendentes");

        using var client = factory.CreateClient();
        var rows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/public/events/{s.EventSlug}/results", Json);

        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("result").ValueKind);
        Assert.Equal("Scheduled", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task WeighIns_ExposeOnlyApprovedEntries()
    {
        using var scope = factory.Services.CreateScope();
        var s = await SeedPastEventAsync(scope.ServiceProvider, "Pesagem Pública");
        // Red approved and over the limit; blue weighed but NOT approved → must stay private.
        await s.Events.SaveWeighInAsync(s.EventId, s.FightId, s.Red.Id,
            new WeighInInput(73.2m, null, true, null));
        await s.Events.SaveWeighInAsync(s.EventId, s.FightId, s.Blue.Id,
            new WeighInInput(80.5m, null, false, null));

        using var client = factory.CreateClient();
        var raw = await client.GetStringAsync($"/api/public/events/{s.EventSlug}/weigh-ins");
        var rows = JsonSerializer.Deserialize<JsonElement>(raw, Json);

        var row = Assert.Single(rows.EnumerateArray());
        Assert.Equal($"{s.Red.FirstName} {s.Red.LastName}", row.GetProperty("athleteName").GetString());
        Assert.Equal(73.2m, row.GetProperty("officialWeightKg").GetDecimal());
        Assert.True(row.GetProperty("missedWeight").GetBoolean());
        // The unapproved weight must not appear anywhere in the payload (ADR-004).
        Assert.DoesNotContain("80.5", raw);
    }

    [Fact]
    public async Task ResultsAndWeighIns_DraftEvent_Are404()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var draft = await events.CreateAsync(new EventInput("Resultados Rascunho", null,
            new DateTime(2026, 7, 2, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/public/events/{draft.Slug}/results")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/public/events/{draft.Slug}/weigh-ins")).StatusCode);
    }
}
