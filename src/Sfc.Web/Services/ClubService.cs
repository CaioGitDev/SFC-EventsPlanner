using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Clubs;
using Sfc.Infrastructure.Images;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Services;

public record ClubInput(string Name, string? City, string? Country,
    string? ContactEmail, string? ContactPhone, string? CoachesText);

public enum ClubDeleteResult
{
    Deleted,
    NotFound,
    HasAthletes,
}

public class ClubService(SfcDbContext db, IImageStorage imageStorage)
{
    private const int LogoMaxDimension = 400;

    public async Task<List<Club>> SearchAsync(string? name, CancellationToken ct = default)
    {
        var query = db.Clubs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{name.Trim()}%"));

        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public Task<Club?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Clubs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Club> CreateAsync(ClubInput input, Stream? logo, CancellationToken ct = default)
    {
        var club = new Club(db.CurrentOrganizationId, input.Name, input.City, input.Country,
            input.ContactEmail, input.ContactPhone);
        club.SetCoaches(ParseCoaches(input.CoachesText));

        if (logo is not null)
            club.SetLogo(await UploadLogoAsync(club.Id, logo, ct));

        db.Clubs.Add(club);
        await db.SaveChangesAsync(ct);
        return club;
    }

    public async Task<Club?> UpdateAsync(Guid id, ClubInput input, Stream? logo,
        CancellationToken ct = default)
    {
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (club is null)
            return null;

        club.Update(input.Name, input.City, input.Country, input.ContactEmail, input.ContactPhone);
        club.SetCoaches(ParseCoaches(input.CoachesText));

        if (logo is not null)
            club.SetLogo(await UploadLogoAsync(club.Id, logo, ct));

        await db.SaveChangesAsync(ct);
        return club;
    }

    public async Task<ClubDeleteResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (club is null)
            return ClubDeleteResult.NotFound;

        if (await db.Athletes.AnyAsync(a => a.ClubId == id, ct))
            return ClubDeleteResult.HasAthletes;

        db.Clubs.Remove(club);
        await db.SaveChangesAsync(ct);

        if (club.LogoUrl is not null)
            await imageStorage.DeleteAsync($"clubs/{club.Id}.webp", ct);

        return ClubDeleteResult.Deleted;
    }

    /// <summary>One coach per line: "Name; contact" — contact optional.</summary>
    public static List<Coach> ParseCoaches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var parts = line.Split(';', 2, StringSplitOptions.TrimEntries);
                return new Coach(parts[0], parts.Length > 1 ? parts[1] : null);
            })
            .ToList();
    }

    private async Task<string> UploadLogoAsync(Guid clubId, Stream logo, CancellationToken ct)
    {
        using var webp = await ImageProcessor.ToWebpAsync(logo, LogoMaxDimension, ct);
        return await imageStorage.SaveAsync(webp, $"clubs/{clubId}.webp", "image/webp", ct);
    }
}
