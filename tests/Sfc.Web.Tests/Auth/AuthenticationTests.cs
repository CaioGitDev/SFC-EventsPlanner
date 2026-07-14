using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Sfc.Web.Tests.Auth;

public class AuthenticationTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    [Fact]
    public async Task AdminPage_WhenAnonymous_RedirectsToLogin()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/Admin/Athletes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WithSeededAdmin_GrantsAccessToAdminArea()
    {
        using var client = factory.CreateClient();

        await AuthTestHelper.LoginAsAdminAsync(client);
        var response = await client.GetAsync("/Admin/Athletes");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsError()
    {
        using var client = factory.CreateClient();
        var token = await AuthTestHelper.GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", "admin@test.local"),
            new KeyValuePair<string, string>("Input.Password", "wrong"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Credenciais inválidas", html);
    }
}
