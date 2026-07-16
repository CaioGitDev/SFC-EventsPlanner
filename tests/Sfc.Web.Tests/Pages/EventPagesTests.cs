using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class EventPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Index_ListsEventsWithStatusBadge()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        await service.CreateAsync(new EventInput("Evento Página Teste", null,
            new DateTime(2026, 12, 10, 20, 0, 0), null, "Lisboa", null, null, null), null, null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync("/Admin/Events");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("Evento Página Teste", html);
        Assert.Contains("Rascunho", html);
    }

    [Fact]
    public async Task Create_PostsFormAndRedirects()
    {
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Admin/Events/Create");

        var response = await client.PostAsync("/Admin/Events/Create", new MultipartFormDataContent
        {
            { new StringContent("Criado Via Form"), "Form.Name" },
            { new StringContent("2026-12-12T20:00"), "Form.Date" },
            { new StringContent("Lisboa"), "Form.City" },
            { new StringContent(token), "__RequestVerificationToken" },
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventService>();
        var results = await service.SearchAsync("Criado Via Form", null);
        var item = Assert.Single(results);
        Assert.Equal(new DateTime(2026, 12, 12, 20, 0, 0), item.Date);
    }

    [Theory]
    [InlineData("/Admin/Events/Create")]
    public async Task FormPages_WhenAuthenticated_Render(string url)
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Edit_ShowsFightCardWithBillingAndAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var athletes = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var red = await athletes.CreateAsync(new AthleteInput("Edit", "Vermelho", null,
            new DateOnly(2000, 1, 1), "Portugal", Sfc.Domain.Athletes.Discipline.MuayThai,
            Sfc.Domain.Athletes.AthleteStatus.Professional, null, null, null, null, null,
            false, null, null), (0, 0, 0, 0), null);
        var blue = await athletes.CreateAsync(new AthleteInput("Edit", "Azul", null,
            new DateOnly(2000, 1, 1), "Portugal", Sfc.Domain.Athletes.Discipline.MuayThai,
            Sfc.Domain.Athletes.AthleteStatus.Professional, null, null, null, null, null,
            false, null, null), (0, 0, 0, 0), null);
        var evt = await events.CreateAsync(new EventInput("Evento Com Card", null,
            new DateTime(2026, 12, 15, 20, 0, 0), null, null, null, null, null), null, null);
        await events.AddFightAsync(evt.Id,
            new FightInput(red.Id, blue.Id, Sfc.Domain.Athletes.Discipline.MuayThai,
                3, 3, "-72kg", null, false, false));

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var response = await client.GetAsync($"/Admin/Events/Edit/{evt.Id}");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("Edit Vermelho", html);
        Assert.Contains("Combate principal", html);
    }

    [Fact]
    public async Task Edit_PublishHandler_TransitionsAndShowsBadge()
    {
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventService>();
        var evt = await events.CreateAsync(new EventInput("Evento Publicável", null,
            new DateTime(2026, 12, 16, 20, 0, 0), null, null, null, null, null), null, null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, $"/Admin/Events/Edit/{evt.Id}");
        var response = await client.PostAsync($"/Admin/Events/Edit/{evt.Id}?handler=Publish",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));
        var followUp = await client.GetAsync($"/Admin/Events/Edit/{evt.Id}");
        var html = System.Net.WebUtility.HtmlDecode(await followUp.Content.ReadAsStringAsync());

        Assert.Contains("Publicado", html);
    }
}
