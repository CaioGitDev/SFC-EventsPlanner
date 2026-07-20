using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Services;

public sealed class RecordingHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];
    public bool Throw { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Throw)
            throw new HttpRequestException("portal down");

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

public class PortalRevalidatorTests
{
    private static PortalRevalidator Create(RecordingHandler handler, string? url,
        string? secret = "seg-redo")
        => new(new HttpClient(handler),
            Options.Create(new PortalOptions { RevalidateUrl = url, RevalidateSecret = secret }),
            NullLogger<PortalRevalidator>.Instance);

    [Fact]
    public async Task TriggerAsync_Configured_PostsPayloadWithSecret()
    {
        var handler = new RecordingHandler();
        var revalidator = Create(handler, "http://portal.test/api/revalidate");

        await revalidator.TriggerAsync("event-published", "sfc-12");

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://portal.test/api/revalidate", request.RequestUri!.ToString());
        Assert.Equal("seg-redo", Assert.Single(request.Headers.GetValues("x-revalidate-secret")));
        Assert.Contains("event-published", body);
        Assert.Contains("sfc-12", body);
    }

    [Fact]
    public async Task TriggerAsync_Unconfigured_DoesNothing()
    {
        var handler = new RecordingHandler();
        var revalidator = Create(handler, url: null);

        await revalidator.TriggerAsync("event-published", "sfc-12");

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task TriggerAsync_PortalDown_DoesNotThrow()
    {
        var handler = new RecordingHandler { Throw = true };
        var revalidator = Create(handler, "http://portal.test/api/revalidate");

        await revalidator.TriggerAsync("event-published", "sfc-12");
    }
}

public class PortalRevalidationWiringTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task PublishingAnEvent_TriggersPortalRevalidation()
    {
        var handler = new RecordingHandler();
        using var wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Portal:RevalidateUrl", "http://portal.test/api/revalidate");
            builder.UseSetting("Portal:RevalidateSecret", "seg-wiring");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<PortalRevalidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler));
        });

        using var scope = wired.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(new EventInput("Revalidação Publicar", null,
            DateTime.Today.AddDays(60).AddHours(20), null, null, null, null, null), null, null);

        await events.PublishAsync(evt.Id);

        var call = Assert.Single(handler.Calls,
            c => c.Body.Contains(evt.Slug) && c.Body.Contains("event-published"));
        Assert.Equal("seg-wiring", Assert.Single(call.Request.Headers.GetValues("x-revalidate-secret")));
    }

    [Fact]
    public async Task CardChangesOnPublicEvent_TriggerRevalidation_ButDraftDoesNot()
    {
        var handler = new RecordingHandler();
        using var wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Portal:RevalidateUrl", "http://portal.test/api/revalidate");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<PortalRevalidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler));
        });

        using var scope = wired.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(new AthleteInput("Card", "Revalida", null,
            new DateOnly(2000, 1, 1), "Portugal", Discipline.MuayThai, AthleteStatus.Professional,
            null, null, null, null, null, false, null, null), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(new AthleteInput("Card", "RevalidaDois", null,
            new DateOnly(2000, 1, 1), "Portugal", Discipline.MuayThai, AthleteStatus.Professional,
            null, null, null, null, null, false, null, null), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Revalidação Card", null,
            DateTime.Today.AddDays(70).AddHours(20), null, null, null, null, null), null, null);

        // Draft: card changes are not public — no revalidation.
        await events.AddFightAsync(evt.Id, new FightInput(red.Id, blue.Id,
            Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        Assert.DoesNotContain(handler.Calls, c => c.Body.Contains("card-changed"));

        await events.PublishAsync(evt.Id);
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;
        handler.Calls.Clear();

        // Published: cancelling a fight is public content changing.
        await events.CancelFightAsync(evt.Id, fightId);
        Assert.Contains(handler.Calls,
            c => c.Body.Contains("card-changed") && c.Body.Contains(evt.Slug));
    }

    [Fact]
    public async Task UpdatingAPublishedEvent_TriggersRevalidation()
    {
        var handler = new RecordingHandler();
        using var wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Portal:RevalidateUrl", "http://portal.test/api/revalidate");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<PortalRevalidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler));
        });

        using var scope = wired.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(new EventInput("Revalidação Update", null,
            DateTime.Today.AddDays(80).AddHours(20), null, null, null, null, null), null, null);
        await events.PublishAsync(evt.Id);
        handler.Calls.Clear();

        // Adding the stream link on event day must reach the portal's home CTA.
        await events.UpdateAsync(evt.Id, new EventInput("Revalidação Update", null,
            DateTime.Today.AddDays(80).AddHours(20), null, null, null,
            "https://youtube.com/watch?v=live", evt.Slug), null, null);

        Assert.Contains(handler.Calls,
            c => c.Body.Contains("event-updated") && c.Body.Contains(evt.Slug));
    }

    [Fact]
    public async Task SavingAResult_TriggersPortalRevalidation()
    {
        var handler = new RecordingHandler();
        using var wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Portal:RevalidateUrl", "http://portal.test/api/revalidate");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<PortalRevalidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler));
        });

        using var scope = wired.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(new AthleteInput("Revalida", "Vermelho", null,
            new DateOnly(2000, 1, 1), "Portugal", Discipline.MuayThai, AthleteStatus.Professional,
            null, null, null, null, null, false, null, null), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(new AthleteInput("Revalida", "Azul", null,
            new DateOnly(2000, 1, 1), "Portugal", Discipline.MuayThai, AthleteStatus.Professional,
            null, null, null, null, null, false, null, null), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Revalidação Resultado", null,
            new DateTime(2026, 7, 5, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id, new FightInput(red.Id, blue.Id,
            Sfc.Domain.Athletes.Discipline.MuayThai, 3, 3, "-72kg", null, false, false));
        var fightId = (await events.GetWithCardAsync(evt.Id))!.Fights[0].Id;
        handler.Calls.Clear();

        await events.SaveResultAsync(evt.Id, fightId,
            new ResultInput(red.Id, Sfc.Domain.Events.FightResultMethod.Ko, 1, null));

        Assert.Contains(handler.Calls, c => c.Body.Contains("result-changed") && c.Body.Contains(evt.Slug));
    }
}
