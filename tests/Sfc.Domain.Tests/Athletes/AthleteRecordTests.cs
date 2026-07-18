using Sfc.Domain.Athletes;
using Xunit;

namespace Sfc.Domain.Tests.Athletes;

public class AthleteRecordTests
{
    private static Athlete CreateAthlete(int wins = 10, int losses = 2, int draws = 1, int kos = 4)
        => new(Guid.NewGuid(), "Ana", "Silva", new DateOnly(1999, 5, 1), "Portugal",
            Discipline.MuayThai, AthleteStatus.Professional, "ana-silva",
            baselineWins: wins, baselineLosses: losses, baselineDraws: draws, baselineKos: kos);

    [Fact]
    public void Record_IsBaselinePlusResultAggregation()
    {
        var athlete = CreateAthlete();

        athlete.ApplyResultDelta(new RecordDelta(1, 0, 0, 1)); // KO win
        athlete.ApplyResultDelta(new RecordDelta(0, 1, 0, 0)); // loss
        athlete.ApplyResultDelta(new RecordDelta(0, 0, 1, 0)); // draw

        Assert.Equal(11, athlete.Wins);
        Assert.Equal(3, athlete.Losses);
        Assert.Equal(2, athlete.Draws);
        Assert.Equal(5, athlete.WinsByKo);
        Assert.Equal("11-3-2", athlete.RecordDisplay);
    }

    [Fact]
    public void ApplyResultDelta_NegatedDelta_RestoresPreviousRecord()
    {
        var athlete = CreateAthlete();
        var delta = new RecordDelta(1, 0, 0, 1);

        athlete.ApplyResultDelta(delta);
        athlete.ApplyResultDelta(delta.Negate());

        Assert.Equal(10, athlete.Wins);
        Assert.Equal(4, athlete.WinsByKo);
    }

    [Fact]
    public void ApplyResultDelta_ThatWouldGoNegative_Throws()
    {
        var athlete = CreateAthlete();

        Assert.Throws<InvalidOperationException>(() =>
            athlete.ApplyResultDelta(new RecordDelta(-1, 0, 0, 0)));
    }

    [Fact]
    public void ZeroDelta_ChangesNothing()
    {
        var athlete = CreateAthlete();

        athlete.ApplyResultDelta(RecordDelta.Zero);

        Assert.Equal("10-2-1", athlete.RecordDisplay);
    }
}
