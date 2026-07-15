using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class AthleteServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static AthleteInput Input(string firstName, string lastName,
        Guid? clubId = null, Discipline discipline = Discipline.MuayThai, string? slug = null)
        => new(firstName, lastName, null, new DateOnly(2000, 5, 20), "Portugal",
            discipline, AthleteStatus.Professional, clubId, null, null, null, null, false, slug, null);

    [Fact]
    public async Task CreateAsync_GeneratesSlugFromName()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();

        var athlete = await service.CreateAsync(Input("Zé", "Slugueiro"), (0, 0, 0, 0), null);

        Assert.Equal("ze-slugueiro", athlete.Slug);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_GetsNumericSuffix()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();

        await service.CreateAsync(Input("Dupla", "Colisão"), (0, 0, 0, 0), null);
        var second = await service.CreateAsync(Input("Dupla", "Colisão"), (0, 0, 0, 0), null);
        var third = await service.CreateAsync(Input("Dupla", "Colisão"), (0, 0, 0, 0), null);

        Assert.Equal("dupla-colisao-2", second.Slug);
        Assert.Equal("dupla-colisao-3", third.Slug);
    }

    [Fact]
    public async Task CreateAsync_WithBaseline_SetsInitialRecord()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();

        var athlete = await service.CreateAsync(Input("Com", "Cartel"), (18, 3, 1, 9), null);

        Assert.Equal("18-3-1", athlete.RecordDisplay);
        Assert.Equal(9, athlete.WinsByKo);
    }

    [Fact]
    public async Task CreateAsync_WithPhoto_StoresWebpAndSetsUrl()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        using var png = await CreatePngAsync(1000, 1000);

        var athlete = await service.CreateAsync(Input("Foto", "Grafado"), (0, 0, 0, 0), png);

        Assert.Equal($"https://media.test.local/athletes/{athlete.Id}.webp", athlete.PhotoUrl);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"athletes/{athlete.Id}.webp"));
    }

    [Fact]
    public async Task UpdateAsync_WithExplicitSlug_KeepsItUniqueExcludingSelf()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var athlete = await service.CreateAsync(Input("Slug", "Próprio"), (0, 0, 0, 0), null);

        // Re-saving with its own slug must not grow a suffix.
        var updated = await service.UpdateAsync(athlete.Id,
            Input("Slug", "Próprio", slug: "slug-proprio"), isActive: true, null);

        Assert.Equal("slug-proprio", updated!.Slug);
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameClubAndDiscipline()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var clubService = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = await clubService.CreateAsync(
            new ClubInput("Clube Filtro", null, null, null, null, null), null);
        await service.CreateAsync(Input("Filtrável", "Alvo", club.Id, Discipline.K1), (0, 0, 0, 0), null);
        await service.CreateAsync(Input("Outro", "Atleta", null, Discipline.Boxing), (0, 0, 0, 0), null);

        var byName = await service.SearchAsync("filtrável alvo", null, null);
        var byClub = await service.SearchAsync(null, club.Id, null);
        var byDiscipline = await service.SearchAsync("Filtrável", null, Discipline.K1);
        var noMatch = await service.SearchAsync("Filtrável", null, Discipline.Boxing);

        Assert.Contains(byName.Items, a => a.FullName == "Filtrável Alvo");
        Assert.Contains(byClub.Items, a => a.ClubName == "Clube Filtro");
        Assert.Contains(byDiscipline.Items, a => a.FullName == "Filtrável Alvo");
        Assert.DoesNotContain(noMatch.Items, a => a.FullName == "Filtrável Alvo");
    }

    [Fact]
    public async Task DeleteAsync_RemovesAthlete()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var athlete = await service.CreateAsync(Input("Para", "Apagar"), (0, 0, 0, 0), null);

        var deleted = await service.DeleteAsync(athlete.Id);

        Assert.True(deleted);
        Assert.Null(await service.GetAsync(athlete.Id));
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
