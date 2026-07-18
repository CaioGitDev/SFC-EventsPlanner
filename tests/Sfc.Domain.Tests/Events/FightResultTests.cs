using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Xunit;

namespace Sfc.Domain.Tests.Events;

public class FightResultTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 7, 18);

    private static Event CreateEvent(DateTime? date = null)
        => new(OrgId, "SFC 12", date ?? new DateTime(2026, 7, 1, 20, 0, 0), "sfc-12");

    private static Fight AddFight(Event evt, out Guid red, out Guid blue)
    {
        red = Guid.NewGuid();
        blue = Guid.NewGuid();
        return evt.AddFight(red, blue, Discipline.MuayThai, rounds: 3, roundDurationMinutes: 3,
            weightClass: "-72kg", catchweightKg: null, isTitleFight: false, isAmateur: false);
    }

    // --- Effects matrix (sfc-contexto) ---

    [Theory]
    [InlineData(FightResultMethod.Ko, 1)]
    [InlineData(FightResultMethod.Tko, 1)]
    [InlineData(FightResultMethod.UnanimousDecision, 0)]
    [InlineData(FightResultMethod.SplitDecision, 0)]
    [InlineData(FightResultMethod.MajorityDecision, 0)]
    [InlineData(FightResultMethod.Disqualification, 0)]
    [InlineData(FightResultMethod.Forfeit, 0)]
    public void GetDeltas_WinnerMethods_WinnerGetsWinLoserGetsLoss(FightResultMethod method, int expectedKos)
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out var blue);
        var needsRound = method is FightResultMethod.Ko or FightResultMethod.Tko;

        var result = evt.RecordResult(fight.Id, red, method,
            needsRound ? 2 : null, needsRound ? "1:34" : null, Today);
        var (redDelta, blueDelta) = result.GetDeltas(red, blue);

        Assert.Equal(new RecordDelta(1, 0, 0, expectedKos), redDelta);
        Assert.Equal(new RecordDelta(0, 1, 0, 0), blueDelta);
    }

    [Fact]
    public void GetDeltas_BlueCornerWinner_AssignsDeltasToBlue()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out var blue);

        var result = evt.RecordResult(fight.Id, blue, FightResultMethod.Ko, 1, "0:45", Today);
        var (redDelta, blueDelta) = result.GetDeltas(red, blue);

        Assert.Equal(new RecordDelta(0, 1, 0, 0), redDelta);
        Assert.Equal(new RecordDelta(1, 0, 0, 1), blueDelta);
    }

    [Fact]
    public void GetDeltas_Draw_BothGetDraw()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out var blue);

        var result = evt.RecordResult(fight.Id, null, FightResultMethod.Draw, null, null, Today);
        var (redDelta, blueDelta) = result.GetDeltas(red, blue);

        Assert.Equal(new RecordDelta(0, 0, 1, 0), redDelta);
        Assert.Equal(new RecordDelta(0, 0, 1, 0), blueDelta);
    }

    [Fact]
    public void GetDeltas_NoContest_ChangesNothing()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out var blue);

        var result = evt.RecordResult(fight.Id, null, FightResultMethod.NoContest, null, null, Today);
        var (redDelta, blueDelta) = result.GetDeltas(red, blue);

        Assert.Equal(RecordDelta.Zero, redDelta);
        Assert.Equal(RecordDelta.Zero, blueDelta);
    }

    // --- Winner validation ---

    [Theory]
    [InlineData(FightResultMethod.Draw)]
    [InlineData(FightResultMethod.NoContest)]
    public void RecordResult_WinnerOnDrawOrNoContest_Throws(FightResultMethod method)
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, red, method, null, null, Today));
    }

    [Fact]
    public void RecordResult_WinnerMethodWithoutWinner_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, null, FightResultMethod.Ko, 1, null, Today));
    }

    [Fact]
    public void RecordResult_WinnerNotInCorners_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, Guid.NewGuid(), FightResultMethod.Ko, 1, null, Today));
    }

    // --- Round/time validation ---

    [Fact]
    public void RecordResult_KoWithoutRound_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, null, null, Today));
    }

    [Fact]
    public void RecordResult_RoundBeyondFightRounds_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _); // 3 rounds

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 4, null, Today));
    }

    [Fact]
    public void RecordResult_DecisionWithRound_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.UnanimousDecision, 3, null, Today));
    }

    [Fact]
    public void RecordResult_ForfeitWithTime_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Forfeit, null, "1:00", Today));
    }

    [Theory]
    [InlineData("1:75")]
    [InlineData("abc")]
    [InlineData("134")]
    public void RecordResult_InvalidTimeFormat_Throws(string time)
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<ArgumentException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 2, time, Today));
    }

    [Fact]
    public void RecordResult_KoWithRoundOnly_Succeeds()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        var result = evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 2, null, Today);

        Assert.Equal(2, result.Round);
        Assert.Null(result.Time);
    }

    [Fact]
    public void RecordResult_DisqualificationWithoutRound_Succeeds()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        var result = evt.RecordResult(fight.Id, red, FightResultMethod.Disqualification, null, null, Today);

        Assert.Null(result.Round);
    }

    // --- State transitions ---

    [Fact]
    public void RecordResult_SetsCompletedAndAttachesResult()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        var result = evt.RecordResult(fight.Id, red, FightResultMethod.Tko, 3, "2:59", Today);

        Assert.Equal(FightStatus.Completed, fight.Status);
        Assert.Same(result, fight.Result);
        Assert.Equal(red, result.WinnerAthleteId);
    }

    [Fact]
    public void RecordResult_NoContestMethod_SetsNoContestStatus()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        evt.RecordResult(fight.Id, null, FightResultMethod.NoContest, null, null, Today);

        Assert.Equal(FightStatus.NoContest, fight.Status);
    }

    [Fact]
    public void RecordResult_OnFightWithResult_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);
        evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today);

        Assert.Throws<InvalidOperationException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today));
    }

    [Fact]
    public void RecordResult_OnCancelledFight_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);
        evt.CancelFight(fight.Id);

        Assert.Throws<InvalidOperationException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today));
    }

    [Fact]
    public void RecordResult_OnCancelledEvent_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);
        evt.Cancel();

        Assert.Throws<InvalidOperationException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today));
    }

    [Fact]
    public void RecordResult_OnFutureEvent_Throws()
    {
        var evt = CreateEvent(new DateTime(2026, 7, 19, 20, 0, 0)); // tomorrow
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<InvalidOperationException>(() =>
            evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today));
    }

    [Fact]
    public void RecordResult_OnEventDatedToday_Succeeds()
    {
        var evt = CreateEvent(new DateTime(2026, 7, 18, 22, 0, 0)); // tonight
        var fight = AddFight(evt, out var red, out _);

        var result = evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today);

        Assert.NotNull(result);
    }

    // --- ChangeResult ---

    [Fact]
    public void ChangeResult_ReplacesValuesAndRederivesStatus()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);
        evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, "1:00", Today);

        var result = evt.ChangeResult(fight.Id, null, FightResultMethod.NoContest, null, null);

        Assert.Equal(FightStatus.NoContest, fight.Status);
        Assert.Null(result.WinnerAthleteId);
        Assert.Null(result.Round);
        Assert.Null(result.Time);
    }

    [Fact]
    public void ChangeResult_FromNoContestToKo_SetsCompleted()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out var blue);
        evt.RecordResult(fight.Id, null, FightResultMethod.NoContest, null, null, Today);

        evt.ChangeResult(fight.Id, blue, FightResultMethod.Ko, 2, "0:30");

        Assert.Equal(FightStatus.Completed, fight.Status);
    }

    [Fact]
    public void ChangeResult_WithoutExistingResult_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);

        Assert.Throws<InvalidOperationException>(() =>
            evt.ChangeResult(fight.Id, red, FightResultMethod.Ko, 1, null));
    }

    // --- DeleteResult ---

    [Fact]
    public void DeleteResult_RemovesResultAndRestoresScheduled()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);
        evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today);

        evt.DeleteResult(fight.Id);

        Assert.Null(fight.Result);
        Assert.Equal(FightStatus.Scheduled, fight.Status);
    }

    [Fact]
    public void DeleteResult_WithoutResult_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        Assert.Throws<InvalidOperationException>(() => evt.DeleteResult(fight.Id));
    }

    // --- Cancel / NoContest / Reinstate ---

    [Fact]
    public void CancelFight_FromScheduled_SetsCancelled()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        evt.CancelFight(fight.Id);

        Assert.Equal(FightStatus.Cancelled, fight.Status);
    }

    [Fact]
    public void MarkFightNoContest_FromScheduled_SetsNoContestWithoutResult()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        evt.MarkFightNoContest(fight.Id);

        Assert.Equal(FightStatus.NoContest, fight.Status);
        Assert.Null(fight.Result);
    }

    [Fact]
    public void CancelFight_FromCompleted_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out var red, out _);
        evt.RecordResult(fight.Id, red, FightResultMethod.Ko, 1, null, Today);

        Assert.Throws<InvalidOperationException>(() => evt.CancelFight(fight.Id));
    }

    [Fact]
    public void ReinstateFight_FromCancelled_RestoresScheduled()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);
        evt.CancelFight(fight.Id);

        evt.ReinstateFight(fight.Id);

        Assert.Equal(FightStatus.Scheduled, fight.Status);
    }

    [Fact]
    public void ReinstateFight_NoContestWithResult_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);
        evt.RecordResult(fight.Id, null, FightResultMethod.NoContest, null, null, Today);

        Assert.Throws<InvalidOperationException>(() => evt.ReinstateFight(fight.Id));
    }

    [Fact]
    public void ReinstateFight_FromScheduled_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt, out _, out _);

        Assert.Throws<InvalidOperationException>(() => evt.ReinstateFight(fight.Id));
    }
}
