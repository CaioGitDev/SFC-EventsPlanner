using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Pages;

public class AthletePagesTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task Index_ListsAthletesWithRecordAndSupportsNameFilter()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        await service.CreateAsync(new AthleteInput("Página", "Listada", "A Lenda",
            new DateOnly(1995, 1, 1), "Portugal", Discipline.MuayThai, AthleteStatus.Professional,
            null, null, null, null, null, false, null), (12, 2, 0, 6), null);

        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var listed = await client.GetAsync("/Admin/Athletes");
        var listedHtml = System.Net.WebUtility.HtmlDecode(await listed.Content.ReadAsStringAsync());
        var filtered = await client.GetAsync("/Admin/Athletes?Search=página listada");
        var filteredHtml = System.Net.WebUtility.HtmlDecode(await filtered.Content.ReadAsStringAsync());
        var noMatch = await client.GetAsync("/Admin/Athletes?Search=inexistente-xyz");
        var noMatchHtml = System.Net.WebUtility.HtmlDecode(await noMatch.Content.ReadAsStringAsync());

        Assert.Contains("Página Listada", listedHtml);
        Assert.Contains("12-2-0", listedHtml);
        Assert.Contains("Página Listada", filteredHtml);
        Assert.DoesNotContain("Página Listada", noMatchHtml);
    }

    [Fact]
    public async Task Create_PostsFormWithBaselineAndRedirects()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await AuthTestHelper.LoginAsAdminAsync(client);
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Admin/Athletes/Create");

        var response = await client.PostAsync("/Admin/Athletes/Create", new MultipartFormDataContent
        {
            { new StringContent("Criado"), "Form.FirstName" },
            { new StringContent("Via Form"), "Form.LastName" },
            { new StringContent("1999-04-15"), "Form.DateOfBirth" },
            { new StringContent("Portugal"), "Form.Nationality" },
            { new StringContent("MuayThai"), "Form.Discipline" },
            { new StringContent("Amateur"), "Form.Status" },
            { new StringContent("3"), "BaselineWins" },
            { new StringContent("1"), "BaselineLosses" },
            { new StringContent("0"), "BaselineDraws" },
            { new StringContent("2"), "BaselineKos" },
            { new StringContent("false"), "Form.PublicProfileConsent" },
            { new StringContent(token), "__RequestVerificationToken" },
        });

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var results = await service.SearchAsync("Criado Via Form", null, null);
        var item = Assert.Single(results.Items);
        Assert.Equal("3-1-0", item.Record);
    }

    [Theory]
    [InlineData("/Admin/Athletes/Create")]
    public async Task FormPages_WhenAuthenticated_Render(string url)
    {
        using var client = factory.CreateClient();
        await AuthTestHelper.LoginAsAdminAsync(client);

        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }
}
