using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
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

        var result = await service.DeleteAsync(athlete.Id);

        Assert.Equal(AthleteDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(athlete.Id));
    }

    [Fact]
    public async Task DeleteAsync_AthleteWithFight_ReturnsHasFights()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var red = await service.CreateAsync(Input("Com", "Combate"), (0, 0, 0, 0), null);
        var blue = await service.CreateAsync(Input("Adversário", "Dele"), (0, 0, 0, 0), null);
        var evt = new Event(db.CurrentOrganizationId, "SFC Guard", new DateTime(2026, 11, 1, 20, 0, 0), "sfc-guard");
        evt.AddFight(red.Id, blue.Id, Discipline.MuayThai, 3, 3, "-72kg", null, false, false);
        db.Events.Add(evt);
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(red.Id);

        Assert.Equal(AthleteDeleteResult.HasFights, result);
        Assert.NotNull(await service.GetAsync(red.Id));
    }

    [Fact]
    public async Task ListActiveOptionsAsync_ReturnsLabelledActiveAthletesOnly()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var active = await service.CreateAsync(Input("Opção", "Ativa"), (3, 1, 0, 2), null);
        var inactive = await service.CreateAsync(Input("Opção", "Inativa"), (0, 0, 0, 0), null);
        await service.UpdateAsync(inactive.Id, Input("Opção", "Inativa"), isActive: false, null);

        var options = await service.ListActiveOptionsAsync("opção", null, null);

        Assert.Contains(options, o => o.Id == active.Id && o.Label.Contains("3-1-0"));
        Assert.DoesNotContain(options, o => o.Id == inactive.Id);
    }

    [Fact]
    public async Task ListActiveOptionsAsync_FiltersByDiscipline()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var boxer = await service.CreateAsync(Input("Filtro", "Boxe", discipline: Discipline.Boxing), (0, 0, 0, 0), null);

        var k1Options = await service.ListActiveOptionsAsync("Filtro Boxe", null, Discipline.K1);
        var boxingOptions = await service.ListActiveOptionsAsync("Filtro Boxe", null, Discipline.Boxing);

        Assert.DoesNotContain(k1Options, o => o.Id == boxer.Id);
        Assert.Contains(boxingOptions, o => o.Id == boxer.Id);
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
