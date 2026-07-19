using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Athletes;
using Sfc.Domain.Common;
using Sfc.Infrastructure.Images;
using Sfc.Infrastructure.Persistence;
using Sfc.Infrastructure.Storage;

namespace Sfc.Web.Services;

public record AthleteInput(string FirstName, string LastName, string? Nickname,
    DateOnly DateOfBirth, string Nationality, Discipline Discipline, AthleteStatus Status,
    Guid? ClubId, string? CoachName, string? WeightClass, decimal? WeightKg, int? HeightCm,
    bool PublicProfileConsent, string? Slug, string? Notes);

public record AthleteListItem(Guid Id, string FullName, string? Nickname, string? PhotoUrl,
    string? ClubName, Discipline Discipline, string Record, bool IsActive);

public record AthleteSearchResult(List<AthleteListItem> Items, int TotalCount, int Page, int PageSize);

public enum AthleteDeleteResult
{
    Deleted,
    NotFound,
    HasFights,
}

public record AthleteOption(Guid Id, string Label);

public class AthleteService(SfcDbContext db, IImageStorage imageStorage)
{
    public const int PageSize = 20;
    private const int PhotoMaxDimension = 800;

    public async Task<AthleteSearchResult> SearchAsync(string? name, Guid? clubId,
        Discipline? discipline, int page = 1, CancellationToken ct = default)
    {
        var query = db.Athletes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var pattern = $"%{name.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.FirstName + " " + a.LastName, pattern) ||
                (a.Nickname != null && EF.Functions.ILike(a.Nickname, pattern)));
        }

        if (clubId is not null)
            query = query.Where(a => a.ClubId == clubId);

        if (discipline is not null)
            query = query.Where(a => a.Discipline == discipline);

        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);

        // Project raw values and build the record string in memory — int-to-string
        // concatenation is not reliably translatable to SQL.
        var rows = await query
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.Nickname,
                a.PhotoUrl,
                ClubName = a.Club != null ? a.Club.Name : null,
                a.Discipline,
                Wins = a.BaselineWins + a.ResultWins,
                Losses = a.BaselineLosses + a.ResultLosses,
                Draws = a.BaselineDraws + a.ResultDraws,
                a.IsActive,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new AthleteListItem(r.Id, $"{r.FirstName} {r.LastName}", r.Nickname,
                r.PhotoUrl, r.ClubName, r.Discipline,
                $"{r.Wins}-{r.Losses}-{r.Draws}", r.IsActive))
            .ToList();

        return new AthleteSearchResult(items, total, page, PageSize);
    }

    public Task<Athlete?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Athletes.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task<Athlete> CreateAsync(AthleteInput input,
        (int Wins, int Losses, int Draws, int Kos) baseline, Stream? photo,
        CancellationToken ct = default)
    {
        var slug = await ResolveSlugAsync(input, excludeId: null, ct);

        var athlete = new Athlete(db.CurrentOrganizationId, input.FirstName, input.LastName,
            input.DateOfBirth, input.Nationality, input.Discipline, input.Status, slug,
            input.Nickname, input.ClubId, input.CoachName, input.WeightClass, input.WeightKg,
            input.HeightCm, input.PublicProfileConsent, input.Notes,
            baseline.Wins, baseline.Losses, baseline.Draws, baseline.Kos);

        if (photo is not null)
            athlete.SetPhoto(await UploadPhotoAsync(athlete.Id, photo, ct));

        db.Athletes.Add(athlete);
        await db.SaveChangesAsync(ct);
        return athlete;
    }

    public async Task<Athlete?> UpdateAsync(Guid id, AthleteInput input, bool isActive,
        Stream? photo, CancellationToken ct = default)
    {
        var athlete = await db.Athletes.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (athlete is null)
            return null;

        athlete.Update(input.FirstName, input.LastName, input.Nickname, input.DateOfBirth,
            input.Nationality, input.Discipline, input.Status, input.ClubId, input.CoachName,
            input.WeightClass, input.WeightKg, input.HeightCm, input.PublicProfileConsent,
            isActive, input.Notes);

        var slug = await ResolveSlugAsync(input, excludeId: id, ct);
        if (slug != athlete.Slug)
            athlete.UpdateSlug(slug);

        if (photo is not null)
            athlete.SetPhoto(await UploadPhotoAsync(athlete.Id, photo, ct));

        await db.SaveChangesAsync(ct);
        return athlete;
    }

    public async Task<AthleteDeleteResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var athlete = await db.Athletes.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (athlete is null)
            return AthleteDeleteResult.NotFound;

        if (await db.Fights.AnyAsync(f => f.RedCornerAthleteId == id || f.BlueCornerAthleteId == id, ct))
            return AthleteDeleteResult.HasFights;

        db.Athletes.Remove(athlete);
        await db.SaveChangesAsync(ct);

        if (athlete.PhotoUrl is not null)
            await imageStorage.DeleteAsync($"athletes/{athlete.Id}.webp", ct);

        return AthleteDeleteResult.Deleted;
    }

    public async Task<List<AthleteOption>> ListActiveOptionsAsync(string? name, Guid? clubId,
        Discipline? discipline, CancellationToken ct = default)
    {
        var query = db.Athletes.AsNoTracking().Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var pattern = $"%{name.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.FirstName + " " + a.LastName, pattern) ||
                (a.Nickname != null && EF.Functions.ILike(a.Nickname, pattern)));
        }

        if (clubId is not null)
            query = query.Where(a => a.ClubId == clubId);

        if (discipline is not null)
            query = query.Where(a => a.Discipline == discipline);

        var rows = await query
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.Nickname,
                ClubName = a.Club != null ? a.Club.Name : null,
                Wins = a.BaselineWins + a.ResultWins,
                Losses = a.BaselineLosses + a.ResultLosses,
                Draws = a.BaselineDraws + a.ResultDraws,
            })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var nickname = r.Nickname is null ? "" : $" '{r.Nickname}'";
                var club = r.ClubName is null ? "" : $" · {r.ClubName}";
                var label = $"{r.LastName}, {r.FirstName}{nickname} — " +
                    $"{r.Wins}-{r.Losses}-{r.Draws}{club}";
                return new AthleteOption(r.Id, label);
            })
            .ToList();
    }

    private async Task<string> ResolveSlugAsync(AthleteInput input, Guid? excludeId,
        CancellationToken ct)
    {
        var baseSlug = SlugGenerator.Generate(
            string.IsNullOrWhiteSpace(input.Slug)
                ? $"{input.FirstName} {input.LastName}"
                : input.Slug);

        var slug = baseSlug;
        var suffix = 2;
        while (await db.Athletes.AnyAsync(
            a => a.Slug == slug && (excludeId == null || a.Id != excludeId), ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private async Task<string> UploadPhotoAsync(Guid athleteId, Stream photo, CancellationToken ct)
    {
        using var webp = await ImageProcessor.ToWebpAsync(photo, PhotoMaxDimension, ct);
        return await imageStorage.SaveAsync(webp, $"athletes/{athleteId}.webp", "image/webp", ct);
    }
}
