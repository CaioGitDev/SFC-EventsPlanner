using Sfc.Domain.Athletes;
using Xunit;

namespace Sfc.Domain.Tests.Athletes;

public class AthleteTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Athlete CreateAthlete(
        int baselineWins = 0, int baselineLosses = 0, int baselineDraws = 0, int baselineKos = 0)
        => new(OrgId, "João", "Peixão", new DateOnly(2000, 5, 20), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao",
            baselineWins: baselineWins, baselineLosses: baselineLosses,
            baselineDraws: baselineDraws, baselineKos: baselineKos);

    [Fact]
    public void Constructor_WithValidData_SetsProperties()
    {
        var athlete = CreateAthlete();

        Assert.NotEqual(Guid.Empty, athlete.Id);
        Assert.Equal(OrgId, athlete.OrganizationId);
        Assert.Equal("João", athlete.FirstName);
        Assert.Equal("Peixão", athlete.LastName);
        Assert.Equal("joao-peixao", athlete.Slug);
        Assert.True(athlete.IsActive);
        Assert.False(athlete.PublicProfileConsent);
    }

    [Fact]
    public void Constructor_InitialRecordEqualsBaseline()
    {
        var athlete = CreateAthlete(baselineWins: 18, baselineLosses: 3, baselineDraws: 1, baselineKos: 9);

        Assert.Equal(18, athlete.Wins);
        Assert.Equal(3, athlete.Losses);
        Assert.Equal(1, athlete.Draws);
        Assert.Equal(9, athlete.WinsByKo);
        Assert.Equal("18-3-1", athlete.RecordDisplay);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Constructor_WithNegativeBaseline_Throws(int wins, int losses, int draws, int kos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAthlete(baselineWins: wins, baselineLosses: losses, baselineDraws: draws, baselineKos: kos));
    }

    [Fact]
    public void Constructor_WithMoreKosThanWins_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateAthlete(baselineWins: 2, baselineKos: 3));
    }

    [Theory]
    [InlineData("", "Peixão")]
    [InlineData("João", "")]
    [InlineData("   ", "Peixão")]
    public void Constructor_WithBlankName_Throws(string firstName, string lastName)
    {
        Assert.Throws<ArgumentException>(() =>
            new Athlete(OrgId, firstName, lastName, new DateOnly(2000, 5, 20), "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, "slug"));
    }

    [Fact]
    public void Constructor_WithFutureDateOfBirth_Throws()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        Assert.Throws<ArgumentException>(() =>
            new Athlete(OrgId, "João", "Peixão", future, "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao"));
    }

    [Fact]
    public void Constructor_WithNonCanonicalSlug_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Athlete(OrgId, "João", "Peixão", new DateOnly(2000, 5, 20), "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, "João Peixão"));
    }

    [Fact]
    public void Update_NeverChangesBaseline()
    {
        var athlete = CreateAthlete(baselineWins: 10, baselineLosses: 2, baselineDraws: 0, baselineKos: 5);

        athlete.Update("Johnny", "Fish", "The Eel", new DateOnly(1999, 1, 1), "Brasil",
            Discipline.K1, AthleteStatus.Amateur, null, "Coach Zé", "-72kg", 71.5m, 180, true, false, null);

        Assert.Equal(10, athlete.Wins);
        Assert.Equal(2, athlete.Losses);
        Assert.Equal(5, athlete.WinsByKo);
        Assert.Equal("Johnny", athlete.FirstName);
        Assert.Equal("The Eel", athlete.Nickname);
        Assert.True(athlete.PublicProfileConsent);
        Assert.False(athlete.IsActive);
    }

    [Fact]
    public void UpdateSlug_WithCanonicalSlug_Changes()
    {
        var athlete = CreateAthlete();

        athlete.UpdateSlug("johnny-fish");

        Assert.Equal("johnny-fish", athlete.Slug);
    }

    [Fact]
    public void UpdateSlug_WithNonCanonicalSlug_Throws()
    {
        var athlete = CreateAthlete();

        Assert.Throws<ArgumentException>(() => athlete.UpdateSlug("Not A Slug"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5.5)]
    public void Update_WithNonPositiveWeight_Throws(double weight)
    {
        var athlete = CreateAthlete();

        Assert.Throws<ArgumentException>(() =>
            athlete.Update("João", "Peixão", null, new DateOnly(2000, 5, 20), "Portugal",
                Discipline.MuayThai, AthleteStatus.Professional, null, null, null,
                (decimal)weight, null, false, true, null));
    }

    [Fact]
    public void Constructor_WithNotes_TrimsValue()
    {
        var athlete = new Athlete(OrgId, "João", "Peixão", new DateOnly(2000, 5, 20), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao",
            publicProfileConsent: false, notes: "  consentimento recebido a 12/07/2026  ");

        Assert.Equal("consentimento recebido a 12/07/2026", athlete.Notes);
    }

    [Fact]
    public void Constructor_WithBlankNotes_NormalizesToNull()
    {
        var athlete = new Athlete(OrgId, "João", "Peixão", new DateOnly(2000, 5, 20), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao",
            publicProfileConsent: false, notes: "   ");

        Assert.Null(athlete.Notes);
    }

    [Fact]
    public void Update_ChangesNotes()
    {
        var athlete = CreateAthlete();

        athlete.Update("João", "Peixão", null, new DateOnly(2000, 5, 20), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, null, null, null, null, null,
            false, true, "consentimento do encarregado de educação recebido");

        Assert.Equal("consentimento do encarregado de educação recebido", athlete.Notes);
    }

    [Fact]
    public void Age_IsComputedFromDateOfBirth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var birth = today.AddYears(-25);
        var athlete = new Athlete(OrgId, "João", "Peixão", birth, "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "joao-peixao");

        Assert.Equal(25, athlete.Age);
    }
}
