using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Infrastructure.Persistence;
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

    [Fact]
    public async Task ImportAsync_WithMistypedHeaderColumn_ReportsErrorAndCreatesNothing()
    {
        // "email_contacto" instead of "contact_email": CsvTable.Read raises ImportException
        // for the unknown column, and SeedImporter's file-level catch must record it (already
        // covered by the ImportException catch) rather than let it escape. This proves the
        // wiring, not just CsvTable.Read in isolation.
        Write("clubs.csv",
            "name,city,email_contacto\nTask2 Typo Gym,Lisboa,geral@typo.pt\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        var error = Assert.Single(report.Errors);
        Assert.Contains("clubs.csv", error);
        Assert.Contains("email_contacto", error);
        Assert.Equal(0, report.CountCreated("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        Assert.Empty(await clubs.SearchAsync("Task2 Typo Gym"));
    }

    [Fact]
    public async Task ImportAsync_WithMalformedQuotingInHeader_ReportsErrorAndContinues()
    {
        // A stray quote inside the header line is invalid per RFC4180 and is not the
        // ImportException CsvTable.Read raises for an unknown column — it is CsvHelper's own
        // BadDataException, thrown by csv.ReadHeader() before CsvTable.Read gets a chance to
        // wrap it. It escapes CsvTable.Read entirely, so the file-level catch in SeedImporter
        // must hold the same "the run never aborts" guarantee as the row-level one.
        Write("clubs.csv", "nam\"e,city\nScorpion,Lisboa\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        var error = Assert.Single(report.Errors);
        Assert.Contains("clubs.csv", error);
        Assert.Contains("BadDataException", error);
        Assert.Equal(0, report.CountCreated("clubs.csv"));
    }

    [Fact]
    public async Task ImportAsync_WithRowExceedingColumnLength_ReportsNonArgumentExceptionAndContinues()
    {
        // Club.Name has no domain-level length rule (SetDetails only requires non-blank), so
        // a name over the 200-char column limit (SfcDbContext: entity.Property(c => c.Name)
        // .HasMaxLength(200)) passes domain validation and is rejected only by Postgres on
        // SaveChangesAsync, as a DbUpdateException. The pre-fix row-level catch (ArgumentException
        // only) would let this escape and abort the whole import; this proves the widened
        // `catch (Exception)` is reachable by something other than ArgumentException, and that
        // a second, valid row in the same file still gets imported afterwards.
        var longName = new string('A', 201);
        Write("clubs.csv", $"name,city\n{longName},Lisboa\nTask2 Second Gym,Porto\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        var error = Assert.Single(report.Errors);
        Assert.Contains("clubs.csv", error);
        Assert.Contains("linha 2", error);
        Assert.Contains("DbUpdateException", error);
        Assert.Equal(1, report.CountCreated("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        Assert.Single(await clubs.SearchAsync("Task2 Second Gym"));
    }

    private const string ClubsHeader =
        "name,city,country,contact_email,contact_phone,coaches\n";

    private const string AthletesHeader =
        "first_name,last_name,nickname,date_of_birth,nationality,club_name,coach_name," +
        "discipline,weight_class,weight_kg,height_cm,status,public_profile_consent," +
        "baseline_wins,baseline_losses,baseline_draws,baseline_kos\n";

    [Fact]
    public async Task ImportAsync_CreatesAthleteLinkedToClubWithBaselineRecord()
    {
        Write("clubs.csv", ClubsHeader + "Task3 Home Gym,Lisboa,Portugal,,,\n");
        Write("athletes.csv", AthletesHeader +
            "Rui,Task3Marques,O Falcão,1998-03-11,Portugal,Task3 Home Gym,Mestre Rui," +
            "MuayThai,-72kg,71.4,178,Professional,1,12,3,1,7\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.CountCreated("athletes.csv"));

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var found = await service.SearchAsync("Task3Marques", null, null);
        var item = Assert.Single(found.Items);
        Assert.Equal("12-3-1", item.Record);
        Assert.Equal("Task3 Home Gym", item.ClubName);
    }

    [Fact]
    public async Task ImportAsync_PreservesMinorWithoutConsent()
    {
        Write("athletes.csv", AthletesHeader +
            "Ana,Task3Menor,,2010-06-02,Portugal,,,Kickboxing,-52kg,51.2,160,Amateur,0,0,0,0,0\n");

        await RunAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var athlete = await db.Athletes.SingleAsync(a => a.LastName == "Task3Menor");
        Assert.False(athlete.PublicProfileConsent);
        Assert.Null(athlete.ClubId);
    }

    [Fact]
    public async Task ImportAsync_WithUnknownClub_ReportsErrorAndSkipsAthlete()
    {
        Write("athletes.csv", AthletesHeader +
            "Zeca,Task3Orfao,,1995-01-01,Portugal,Ginásio Que Não Existe,,Boxing,,,,Amateur,1,0,0,0,0\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("Ginásio Que Não Existe"));
        Assert.Equal(0, report.CountCreated("athletes.csv"));
    }

    [Fact]
    public async Task ImportAsync_IsIdempotentByNameAndDateOfBirth()
    {
        Write("athletes.csv", AthletesHeader +
            "Beto,Task3Repete,,1993-09-09,Portugal,,,MMA,,,,Professional,1,4,4,0,2\n");

        await RunAsync();
        var second = await RunAsync();

        Assert.Equal(0, second.CountCreated("athletes.csv"));
        Assert.Equal(1, second.CountSkipped("athletes.csv"));
    }

    [Fact]
    public async Task ImportAsync_WithKosAboveWins_ReportsErrorWithLineNumber()
    {
        Write("athletes.csv", AthletesHeader +
            "Nuno,Task3Invalido,,1990-01-01,Portugal,,,Boxing,,,,Professional,1,2,0,0,5\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("athletes.csv") && e.Contains("linha 2"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
