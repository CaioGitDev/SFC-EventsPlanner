using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Sfc.Web.Services;

public class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Next.js on-demand revalidation endpoint; unset = revalidation disabled (dev default).</summary>
    public string? RevalidateUrl { get; set; }

    public string? RevalidateSecret { get; set; }
}

/// <summary>
/// Tells the portal to revalidate its ISR pages when public content changes
/// (publications, results, weigh-ins). Best-effort by design: failures are logged
/// and NEVER break the backoffice operation that triggered them.
/// </summary>
public class PortalRevalidator(HttpClient httpClient, IOptions<PortalOptions> options,
    ILogger<PortalRevalidator> logger)
{
    public async Task TriggerAsync(string reason, string? eventSlug, CancellationToken ct = default)
    {
        var portal = options.Value;
        if (string.IsNullOrWhiteSpace(portal.RevalidateUrl))
            return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, portal.RevalidateUrl)
            {
                Content = JsonContent.Create(new { reason, eventSlug }),
            };
            if (!string.IsNullOrWhiteSpace(portal.RevalidateSecret))
                request.Headers.Add("x-revalidate-secret", portal.RevalidateSecret);

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Portal revalidation returned {Status} for {Reason} ({Slug})",
                    (int)response.StatusCode, reason, eventSlug);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Portal revalidation failed for {Reason} ({Slug})",
                reason, eventSlug);
        }
    }
}
