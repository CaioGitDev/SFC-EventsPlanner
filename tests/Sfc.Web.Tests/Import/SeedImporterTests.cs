using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Events;
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

    [Fact]
    public async Task ImportAsync_BuildsCompletedEventAndAggregatesRecords()
    {
        Write("athletes.csv", AthletesHeader +
            "Ivo,Task4Vencedor,,1996-05-05,Portugal,,,MuayThai,-72kg,71.0,177,Professional,1,5,1,0,3\n" +
            "Hugo,Task4Derrotado,,1997-07-07,Portugal,,,MuayThai,-72kg,71.5,175,Professional,1,4,2,0,1\n");
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Fight Night,task4-fight-night,2026-05-30T20:00,Pavilhão,Almada,Completed\n");
        Write("fights.csv",
            "event_slug,order,discipline,rounds,round_duration_minutes,weight_class," +
            "catchweight_kg,is_title_fight,is_amateur,red_athlete_slug,blue_athlete_slug\n" +
            "task4-fight-night,1,MuayThai,3,3,-72kg,,0,0,ivo-task4vencedor,hugo-task4derrotado\n");
        Write("results.csv",
            "event_slug,fight_order,winner_slug,method,round,time\n" +
            "task4-fight-night,1,ivo-task4vencedor,Ko,2,1:45\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.CountCreated("events.csv"));
        Assert.Equal(1, report.CountCreated("fights.csv"));
        Assert.Equal(1, report.CountCreated("results.csv"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var winner = await db.Athletes.SingleAsync(a => a.LastName == "Task4Vencedor");
        Assert.Equal("6-1-0", winner.RecordDisplay);   // baseline 5-1-0 + 1 vitória
        Assert.Equal(4, winner.WinsByKo);              // baseline 3 + 1 KO

        var loser = await db.Athletes.SingleAsync(a => a.LastName == "Task4Derrotado");
        Assert.Equal("4-3-0", loser.RecordDisplay);    // baseline 4-2-0 + 1 derrota

        var evt = await db.Events.SingleAsync(e => e.Slug == "task4-fight-night");
        Assert.Equal(EventStatus.Completed, evt.Status);
    }

    [Fact]
    public async Task ImportAsync_WithUnknownAthleteSlug_ReportsErrorWithLineNumber()
    {
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Broken,task4-broken,2026-06-30T20:00,Pavilhão,Almada,Published\n");
        Write("fights.csv",
            "event_slug,order,discipline,rounds,round_duration_minutes,weight_class," +
            "catchweight_kg,is_title_fight,is_amateur,red_athlete_slug,blue_athlete_slug\n" +
            "task4-broken,1,Boxing,3,3,-67kg,,0,1,nao-existe,tambem-nao-existe\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("fights.csv") && e.Contains("nao-existe"));
    }

    [Fact]
    public async Task ImportAsync_StoresEventDateAsLisbonWallClockDuringDst()
    {
        // 2026-05-30 falls in Portugal's DST season (WEST, UTC+1). The old parsing
        // (AdjustToUniversal + AssumeUniversal, then SpecifyKind(Unspecified)) normalised
        // the digits to UTC first, silently shifting a "20:00" CSV value by one hour before
        // storing it — invisible in winter, wrong every summer. The fix takes the digits
        // exactly as written, with no time-zone math at all.
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Dst Night,task4-dst-night,2026-05-30T20:00,Pavilhão,Almada,Draft\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var evt = await db.Events.SingleAsync(e => e.Slug == "task4-dst-night");

        Assert.Equal(new DateTime(2026, 5, 30, 20, 0, 0), evt.Date);
        Assert.Equal(DateTimeKind.Unspecified, evt.Date.Kind);
    }

    [Fact]
    public async Task ImportAsync_WithUtcSuffixInDate_RejectsWithPortugueseErrorInsteadOfGuessing()
    {
        // Event.Date is a naive Europe/Lisbon wall-clock value (no time zone stored) — see
        // docs/plans/2026-07-15-eventos-fightcard-design.md. A "Z" suffix is ambiguous
        // (is 20:00Z meant literally, or does it need converting to Lisbon time?) and
        // reinterpreting it silently is what caused the DST bug; it must be rejected instead.
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Utc Night,task4-utc-night,2026-05-30T20:00:00Z,Pavilhão,Almada,Draft\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e =>
            e.Contains("events.csv") && e.Contains("linha 2") && e.Contains("coluna 'date'"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        Assert.False(await db.Events.AnyAsync(e => e.Slug == "task4-utc-night"));
    }

    [Fact]
    public void EnsureTransitionSucceeded_WithSuccess_DoesNotThrow()
    {
        var row = new CsvRow("events.csv", 2, new Dictionary<string, string>());

        var exception = Record.Exception(() =>
            SeedImporter.EnsureTransitionSucceeded(row, "publicar", EventTransitionResult.Success));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureTransitionSucceeded_WithNonSuccessResult_ThrowsPortugueseErrorNamingOperationAndValue()
    {
        // PublishAsync/CompleteAsync return EventTransitionResult, but SeedImporter used to
        // discard it (unlike AddFightAsync/SaveResultAsync in the same file, which already
        // check theirs). A freshly created event is always Draft, so Publish always succeeds
        // and Complete always follows a successful Publish in the same row — the failure
        // branch is unreachable through the public ImportAsync surface today, so this pure
        // unit test on the extracted guard is the only way to prove it actually fires.
        var row = new CsvRow("events.csv", 2, new Dictionary<string, string>());

        var ex = Assert.Throws<ImportException>(() =>
            SeedImporter.EnsureTransitionSucceeded(row, "publicar", EventTransitionResult.InvalidTransition));

        Assert.Contains("events.csv", ex.Message);
        Assert.Contains("linha 2", ex.Message);
        Assert.Contains("publicar", ex.Message);
        Assert.Contains("InvalidTransition", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_WithFightRowForUnmatchedEventSlug_ReportsOrphanRowError()
    {
        // event_slug in fights.csv/results.csv is matched by filtering per event inside
        // AddFightsAsync/SaveResultsAsync — a typo'd slug that matches nothing in events.csv
        // is never visited by any loop, so it silently vanished with no error and no count.
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Real Card,task4-real-card,2026-05-30T20:00,Pavilhão,Almada,Draft\n");
        Write("fights.csv",
            "event_slug,order,discipline,rounds,round_duration_minutes,weight_class," +
            "catchweight_kg,is_title_fight,is_amateur,red_athlete_slug,blue_athlete_slug\n" +
            "task4-nao-existe,1,Boxing,3,3,-67kg,,0,0,a,b\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e =>
            e.Contains("fights.csv") && e.Contains("linha 2") && e.Contains("task4-nao-existe"));
    }

    [Fact]
    public async Task ImportAsync_WithResultRowForUnmatchedEventSlug_ReportsOrphanRowError()
    {
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Real Card 2,task4-real-card-2,2026-05-30T20:00,Pavilhão,Almada,Draft\n");
        Write("results.csv",
            "event_slug,fight_order,winner_slug,method,round,time\n" +
            "task4-nao-existe-tambem,1,a,Ko,2,1:45\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e =>
            e.Contains("results.csv") && e.Contains("linha 2") && e.Contains("task4-nao-existe-tambem"));
    }

    [Fact]
    public async Task ImportAsync_WithCancelledStatus_ReportsErrorInsteadOfImportingAsDraft()
    {
        // status=Cancelled used to be imported as a plain Draft event with no transition and
        // no error — a silent mismatch between what the file asked for and what got stored.
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Cancelled Night,task4-cancelled-night,2026-05-30T20:00,Pavilhão,Almada,Cancelled\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e =>
            e.Contains("events.csv") && e.Contains("linha 2") && e.Contains("Cancelled"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
