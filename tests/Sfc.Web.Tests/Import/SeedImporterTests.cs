using Microsoft.Extensions.DependencyInjection;
using Sfc.Web.Import;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Import;

public class SeedImporterTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sfc-import").FullName;

    private void Write(string name, string content)
        => File.WriteAllText(Path.Combine(_dir, name), content);

    private async Task<ImportReport> RunAsync(bool dryRun = false)
    {
        using var scope = factory.Services.CreateScope();
        var importer = ActivatorUtilities.CreateInstance<SeedImporter>(scope.ServiceProvider);
        return await importer.ImportAsync(_dir, dryRun);
    }

    [Fact]
    public async Task ImportAsync_CreatesClubsWithCoaches()
    {
        Write("clubs.csv",
            "name,city,country,contact_email,contact_phone,coaches\n" +
            "Task2 Scorpion Gym,Lisboa,Portugal,geral@scorpion.pt,912345678,Mestre Rui; rui@scorpion.pt|Kru Ana\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.CountCreated("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = Assert.Single(await clubs.SearchAsync("Task2 Scorpion Gym"));
        Assert.Equal("Lisboa", club.City);
        Assert.Equal(2, club.Coaches.Count);
        Assert.Equal("Mestre Rui", club.Coaches[0].Name);
        Assert.Equal("rui@scorpion.pt", club.Coaches[0].Contact);
        Assert.Equal("Kru Ana", club.Coaches[1].Name);
    }

    [Fact]
    public async Task ImportAsync_IsIdempotentByClubName()
    {
        Write("clubs.csv", "name,city\nTask2 Repeat Gym,Porto\n");

        await RunAsync();
        var second = await RunAsync();

        Assert.Equal(0, second.CountCreated("clubs.csv"));
        Assert.Equal(1, second.CountSkipped("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        Assert.Single(await clubs.SearchAsync("Task2 Repeat Gym"));
    }

    [Fact]
    public async Task ImportAsync_WithDryRun_WritesNothing()
    {
        Write("clubs.csv", "name,city\nTask2 Ghost Gym,Faro\n");

        var report = await RunAsync(dryRun: true);

        Assert.Equal(1, report.CountCreated("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        Assert.Empty(await clubs.SearchAsync("Task2 Ghost Gym"));
    }

    [Fact]
    public async Task ImportAsync_WithBlankName_ReportsErrorAndContinues()
    {
        Write("clubs.csv", "name,city\n,Braga\nTask2 Valid Gym,Braga\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("clubs.csv") && e.Contains("linha 2"));
        Assert.Equal(1, report.CountCreated("clubs.csv"));
    }

    [Fact]
    public async Task ImportAsync_WithMalformedCoach_ReportsDomainErrorFramedInPortuguese()
    {
        // ";alguem@x.pt" splits into an empty coach name and a contact, which the Coach
        // constructor rejects with an English ArgumentException. The report must frame
        // that in pt-PT for the non-technical human reading it, with no doubled period.
        Write("clubs.csv",
            "name,city,coaches\nTask2 Broken Gym,Lisboa,;alguem@x.pt|Kru Ana\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        var error = Assert.Single(report.Errors);
        Assert.Contains("clubs.csv", error);
        Assert.Contains("linha 2", error);
        Assert.Contains("rejeitado pela validação do domínio", error);
        Assert.Contains("ArgumentException", error);
        Assert.Contains("Coach name is required", error);
        Assert.DoesNotContain("..", error);
        Assert.Equal(0, report.CountCreated("clubs.csv"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
