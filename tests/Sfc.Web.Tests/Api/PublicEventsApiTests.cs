using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Api;

public class PublicEventsApiTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static AthleteInput AthleteInput(string first, string last, bool consent,
        string? nickname = null)
        => new(first, last, nickname, new DateOnly(2000, 1, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            consent, null, null);

    /// <summary>Published event (+30 days unless overridden) with one fight:
    /// red corner consents to a public profile, blue corner does not.</summary>
    private async Task<(string Slug, Athlete Consenting, Athlete Private)> SeedPublishedEventAsync(
        IServiceProvider services, string name, DateTime? date = null)
    {
        var events = services.GetRequiredService<EventService>();
        var athletes = services.GetRequiredService<AthleteService>();

        var consenting = await athletes.CreateAsync(
            AthleteInput(name, "Consentido", consent: true, nickname: "Turbina"), (12, 2, 1, 5), null);
        var noConsent = await athletes.CreateAsync(
            AthleteInput(name, "Privado", consent: false), (3, 0, 0, 1), null);
        var evt = await events.CreateAsync(new EventInput(name, "Gala de teste",
            date ?? DateTime.Today.AddDays(30).AddHours(20), "Pavilhão", "Lisboa",
            "https://tickets.example/x", null, null), null, null);
        await events.AddFightAsync(evt.Id, new FightInput(consenting.Id, noConsent.Id,
            Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        await events.PublishAsync(evt.Id);
        return (evt.Slug, consenting, noConsent);
    }

    [Fact]
    public async Task EventDetail_NeverExposesSensitiveDataOrInternalIds()
    {
        using var scope = factory.Services.CreateScope();
        var (slug, _, _) = await SeedPublishedEventAsync(scope.ServiceProvider, "API Rgpd");

        using var client = factory.CreateClient();
        var raw = await client.GetStringAsync($"/api/public/events/{slug}");

        Assert.DoesNotContain("dateOfBirth", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", raw, StringComparison.OrdinalIgnoreCase);
        // Slugs are the only public keys — no entity GUIDs in the payload.
        Assert.DoesNotMatch(
            new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
                RegexOptions.IgnoreCase), raw);
    }

    [Fact]
    public async Task EventDetail_RedactsAthleteWithoutConsentToNameOnly()
    {
        using var scope = factory.Services.CreateScope();
        var (slug, consenting, noConsent) = await SeedPublishedEventAsync(
            scope.ServiceProvider, "API Consentimento");

        using var client = factory.CreateClient();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/public/events/{slug}", Json);

        var fight = detail.GetProperty("fights")[0];
        var red = fight.GetProperty("red");
        var blue = fight.GetProperty("blue");

        Assert.Equal($"{consenting.FirstName} {consenting.LastName}", red.GetProperty("name").GetString());
        Assert.Equal("Turbina", red.GetProperty("nickname").GetString());
        Assert.Equal(consenting.Slug, red.GetProperty("slug").GetString());
        Assert.Equal("12-2-1", red.GetProperty("record").GetString());
        Assert.Equal(26, red.GetProperty("age").GetInt32());

        Assert.Equal($"{noConsent.FirstName} {noConsent.LastName}", blue.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, blue.GetProperty("slug").ValueKind);
        Assert.Equal(JsonValueKind.Null, blue.GetProperty("record").ValueKind);
        Assert.Equal(JsonValueKind.Null, blue.GetProperty("age").ValueKind);
        Assert.Equal(JsonValueKind.Null, blue.GetProperty("nationality").ValueKind);
        Assert.Equal(JsonValueKind.Null, blue.GetProperty("photoUrl").ValueKind);
    }

    [Fact]
    public async Task DraftEvent_IsInvisible()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var draft = await events.CreateAsync(new EventInput("API Rascunho", null,
            DateTime.Today.AddDays(40), null, null, null, null, null), null, null);

        using var client = factory.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/public/events", Json);
        var response = await client.GetAsync($"/api/public/events/{draft.Slug}");

        var allSlugs = list.GetProperty("upcoming").EnumerateArray()
            .Concat(list.GetProperty("past").EnumerateArray())
            .Select(e => e.GetProperty("slug").GetString());
        Assert.DoesNotContain(draft.Slug, allSlugs);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelledEvent_IsVisibleWithStatus()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(new EventInput("API Cancelado", null,
            DateTime.Today.AddDays(50), null, null, null, null, null), null, null);
        await events.PublishAsync(evt.Id);
        await events.CancelAsync(evt.Id);

        using var client = factory.CreateClient();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/public/events/{evt.Slug}", Json);

        Assert.Equal("Cancelled", detail.GetProperty("status").GetString());
    }

    [Fact]
    public async Task NextEvent_PicksClosestFuturePublished()
    {
        using var scope = factory.Services.CreateScope();
        // +1 day: the soonest future published event this class seeds (others use +30/+40/+50).
        var (slug, _, _) = await SeedPublishedEventAsync(
            scope.ServiceProvider, "API Próximo", DateTime.Today.AddDays(1).AddHours(20));

        using var client = factory.CreateClient();
        var next = await client.GetFromJsonAsync<JsonElement>("/api/public/events/next", Json);

        Assert.Equal(slug, next.GetProperty("slug").GetString());
        Assert.Equal(1, next.GetProperty("fightCount").GetInt32());
    }

    [Fact]
    public async Task EventsList_SplitsUpcomingAndPast()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var past = await events.CreateAsync(new EventInput("API Passado", null,
            new DateTime(2026, 7, 1, 20, 0, 0), null, null, null, null, null), null, null);
        await events.PublishAsync(past.Id);
        await events.CompleteAsync(past.Id);
        var (upcomingSlug, _, _) = await SeedPublishedEventAsync(
            scope.ServiceProvider, "API Futuro", DateTime.Today.AddDays(35).AddHours(20));

        using var client = factory.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/public/events", Json);

        Assert.Contains(past.Slug, list.GetProperty("past").EnumerateArray()
            .Select(e => e.GetProperty("slug").GetString()));
        Assert.Contains(upcomingSlug, list.GetProperty("upcoming").EnumerateArray()
            .Select(e => e.GetProperty("slug").GetString()));
    }
}

/// <summary>Own factory (clean database) so "no upcoming events" is deterministic.</summary>
public class PublicNextEventEmptyTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task NextEvent_WithoutCandidates_Returns204()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/public/events/next");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
