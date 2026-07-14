using System.Text.RegularExpressions;

namespace Sfc.Web.Tests;

public static partial class AuthTestHelper
{
    [GeneratedRegex(
        """
        name="__RequestVerificationToken"[^>]*value="([^"]+)"
        """)]
    private static partial Regex AntiforgeryTokenRegex();

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string getUrl)
    {
        var response = await client.GetAsync(getUrl);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex().Match(html);
        if (!match.Success)
            throw new InvalidOperationException($"No antiforgery token found in {getUrl}.");
        return match.Groups[1].Value;
    }

    public static async Task LoginAsAdminAsync(HttpClient client)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Input.Email", "admin@test.local"),
            new KeyValuePair<string, string>("Input.Password", "Test-Admin-2026!"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]));

        if (response.StatusCode != System.Net.HttpStatusCode.Redirect &&
            !response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login failed: {response.StatusCode}");
        }
    }
}
