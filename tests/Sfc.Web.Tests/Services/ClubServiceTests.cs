using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Athletes;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Sfc.Web.Tests.Services;

public class ClubServiceTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static ClubInput Input(string name = "Team Scorpion") =>
        new(name, "Lisboa", "Portugal", null, null, "Mestre Rui; rui@scorpion.pt\nKru Ana");

    [Fact]
    public void ParseCoaches_ParsesOnePerLineWithOptionalContact()
    {
        var coaches = ClubService.ParseCoaches("Mestre Rui; rui@scorpion.pt\n\nKru Ana\n  ");

        Assert.Equal(2, coaches.Count);
        Assert.Equal("Mestre Rui", coaches[0].Name);
        Assert.Equal("rui@scorpion.pt", coaches[0].Contact);
        Assert.Equal("Kru Ana", coaches[1].Name);
        Assert.Null(coaches[1].Contact);
    }

    [Fact]
    public void ParseCoaches_WithNull_ReturnsEmpty()
    {
        Assert.Empty(ClubService.ParseCoaches(null));
    }

    [Fact]
    public async Task CreateAsync_PersistsClubWithCoaches()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();

        var club = await service.CreateAsync(Input(), logo: null);

        var loaded = await service.GetAsync(club.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Team Scorpion", loaded.Name);
        Assert.Equal(2, loaded.Coaches.Count);
    }

    [Fact]
    public async Task CreateAsync_WithLogo_ProcessesAndStoresWebp()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        using var png = await CreatePngAsync(600, 600);

        var club = await service.CreateAsync(Input("Logo Club"), png);

        Assert.Equal($"https://media.test.local/clubs/{club.Id}.webp", club.LogoUrl);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"clubs/{club.Id}.webp"));
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameCaseInsensitive()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        await service.CreateAsync(Input("Search Target Gym"), null);

        var results = await service.SearchAsync("search target");

        Assert.Contains(results, c => c.Name == "Search Target Gym");
    }

    [Fact]
    public async Task UpdateAsync_ChangesDetailsAndCoaches()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = await service.CreateAsync(Input("Before Update"), null);

        var updated = await service.UpdateAsync(club.Id,
            new ClubInput("After Update", "Porto", "Portugal", "x@y.pt", null, "Novo Treinador"), null);

        Assert.NotNull(updated);
        Assert.Equal("After Update", updated.Name);
        var coach = Assert.Single(updated.Coaches);
        Assert.Equal("Novo Treinador", coach.Name);
    }

    [Fact]
    public async Task DeleteAsync_WithAthletes_ReturnsHasAthletes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var club = await service.CreateAsync(Input("Blocked Delete"), null);
        db.Athletes.Add(new Athlete(db.CurrentOrganizationId, "Ana", "Silva",
            new DateOnly(1998, 3, 1), "Portugal", Discipline.K1, AthleteStatus.Amateur,
            "ana-silva-blocked-delete", clubId: club.Id));
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(club.Id);

        Assert.Equal(ClubDeleteResult.HasAthletes, result);
        Assert.NotNull(await service.GetAsync(club.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithoutAthletes_Deletes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = await service.CreateAsync(Input("Free To Delete"), null);

        var result = await service.DeleteAsync(club.Id);

        Assert.Equal(ClubDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(club.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithLogo_RemovesStoredImage()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClubService>();
        using var png = await CreatePngAsync(100, 100);
        var club = await service.CreateAsync(Input("Delete With Logo"), png);
        Assert.True(factory.ImageStorage.Saved.ContainsKey($"clubs/{club.Id}.webp"));

        var result = await service.DeleteAsync(club.Id);

        Assert.Equal(ClubDeleteResult.Deleted, result);
        Assert.False(factory.ImageStorage.Saved.ContainsKey($"clubs/{club.Id}.webp"));
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
