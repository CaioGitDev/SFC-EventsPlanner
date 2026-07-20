using Sfc.Web.Services;

namespace Sfc.Web.Api;

/// <summary>
/// Read-only anonymous API consumed by the Next.js portal via SSG/ISR.
/// Slugs are the only public keys; GDPR rules (ADR-004) are enforced in
/// <see cref="PublicContentService"/> and covered by integration tests.
/// </summary>
public static class PublicApi
{
    public static void MapPublicApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/public").AllowAnonymous();

        api.MapGet("/events/next", async (PublicContentService content, CancellationToken ct) =>
        {
            var next = await content.GetNextEventAsync(ct);
            return next is null ? Results.NoContent() : Results.Ok(next);
        });

        api.MapGet("/events", async (PublicContentService content, CancellationToken ct)
            => Results.Ok(await content.GetEventsAsync(ct)));

        api.MapGet("/events/{slug}", async (string slug, PublicContentService content,
            CancellationToken ct) =>
        {
            var detail = await content.GetEventAsync(slug, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        api.MapGet("/fighters/{slug}", async (string slug, PublicContentService content,
            CancellationToken ct) =>
        {
            var profile = await content.GetFighterAsync(slug, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });
    }
}
