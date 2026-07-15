using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class ClubPagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Index_WhenAuthenticated_ListsClubs()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        await service.CreateAsync(new ClubInput("Clube Página Teste", "Lisboa", "Portugal",
            null, null, null), null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync("/Admin/Clubs");
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("Clube Página Teste", html);
    }

    [Theory]
    [InlineData("/Admin/Clubs/Create")]
    public async Task FormPages_WhenAuthenticated_Render(string url)
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Create_PostsFormAndRedirectsToIndex()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Admin/Clubs/Create");

        var response = await client.PostAsync("/Admin/Clubs/Create", new MultipartFormDataContent
        {
            { new StringContent("Clube Criado Via Form"), "Form.Name" },
            { new StringContent("Lisboa"), "Form.City" },
            { new StringContent("Treinador A; 912"), "Form.CoachesText" },
            { new StringContent(token), "__RequestVerificationToken" },
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var results = await service.SearchAsync("Clube Criado Via Form");
        var club = Assert.Single(results);
        var coach = Assert.Single(club.Coaches);
        Assert.Equal("Treinador A", coach.Name);
    }
}
