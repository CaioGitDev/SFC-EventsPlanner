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
}
