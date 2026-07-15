using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Xunit;

namespace Sfc.Domain.Tests.Events;

public class FightCardTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Event CreateEvent()
        => new(OrgId, "SFC 12", new DateTime(2026, 9, 12, 20, 0, 0), "sfc-12");

    private static Fight AddFight(Event evt, Guid? red = null, Guid? blue = null)
        => evt.AddFight(red ?? Guid.NewGuid(), blue ?? Guid.NewGuid(), Discipline.MuayThai,
            rounds: 3, roundDurationMinutes: 3, weightClass: "-72kg", catchweightKg: null,
            isTitleFight: false, isAmateur: false);

    [Fact]
    public void AddFight_AppendsWithContiguousOrder()
    {
        var evt = CreateEvent();

        var first = AddFight(evt);
        var second = AddFight(evt);
        var third = AddFight(evt);

        Assert.Equal(1, first.Order);
        Assert.Equal(2, second.Order);
        Assert.Equal(3, third.Order);
        Assert.Equal(3, evt.Fights.Count);
    }

    [Fact]
    public void AddFight_DerivesBilling_LastIsMainSecondToLastIsCoMain()
    {
        var evt = CreateEvent();

        var f1 = AddFight(evt);
        Assert.Equal(FightBilling.Main, f1.Billing); // single fight is the main event

        var f2 = AddFight(evt);
        Assert.Equal(FightBilling.CoMain, f1.Billing);
        Assert.Equal(FightBilling.Main, f2.Billing);

        var f3 = AddFight(evt);
        Assert.Equal(FightBilling.Card, f1.Billing);
        Assert.Equal(FightBilling.CoMain, f2.Billing);
        Assert.Equal(FightBilling.Main, f3.Billing);
    }

    [Fact]
    public void AddFight_WithSameAthleteBothCorners_Throws()
    {
        var evt = CreateEvent();
        var athlete = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => AddFight(evt, red: athlete, blue: athlete));
    }

    [Fact]
    public void AddFight_WithAthleteAlreadyInCard_Throws()
    {
        var evt = CreateEvent();
        var repeated = Guid.NewGuid();
        AddFight(evt, red: repeated);

        Assert.Throws<InvalidOperationException>(() => AddFight(evt, blue: repeated));
    }

    [Fact]
    public void AddFight_WithBothWeightClassAndCatchweight_Throws()
    {
        var evt = CreateEvent();

        Assert.Throws<ArgumentException>(() =>
            evt.AddFight(Guid.NewGuid(), Guid.NewGuid(), Discipline.K1, 3, 3,
                weightClass: "-72kg", catchweightKg: 74.5m, isTitleFight: false, isAmateur: false));
    }

    [Fact]
    public void AddFight_WithNeitherWeightClassNorCatchweight_Throws()
    {
        var evt = CreateEvent();

        Assert.Throws<ArgumentException>(() =>
            evt.AddFight(Guid.NewGuid(), Guid.NewGuid(), Discipline.K1, 3, 3,
                weightClass: null, catchweightKg: null, isTitleFight: false, isAmateur: false));
    }

    [Fact]
    public void MoveFight_Down_SwapsWithNextAndRederivesBilling()
    {
        var evt = CreateEvent();
        var f1 = AddFight(evt);
        var f2 = AddFight(evt);
        var f3 = AddFight(evt);

        var moved = evt.MoveFight(f2.Id, MoveDirection.Down);

        Assert.True(moved);
        Assert.Equal(3, f2.Order);
        Assert.Equal(2, f3.Order);
        Assert.Equal(FightBilling.Main, f2.Billing);
        Assert.Equal(FightBilling.CoMain, f3.Billing);
        Assert.Equal(FightBilling.Card, f1.Billing);
    }

    [Fact]
    public void MoveFight_UpAtFirstPosition_ReturnsFalse()
    {
        var evt = CreateEvent();
        var f1 = AddFight(evt);
        AddFight(evt);

        Assert.False(evt.MoveFight(f1.Id, MoveDirection.Up));
        Assert.Equal(1, f1.Order);
    }

    [Fact]
    public void MoveFight_UnknownFight_Throws()
    {
        var evt = CreateEvent();
        AddFight(evt);

        Assert.Throws<InvalidOperationException>(() => evt.MoveFight(Guid.NewGuid(), MoveDirection.Up));
    }

    [Fact]
    public void RemoveFight_ClosesOrderGapAndRederivesBilling()
    {
        var evt = CreateEvent();
        var f1 = AddFight(evt);
        var f2 = AddFight(evt);
        var f3 = AddFight(evt);

        var removed = evt.RemoveFight(f2.Id);

        Assert.True(removed);
        Assert.Equal(2, evt.Fights.Count);
        Assert.Equal(1, f1.Order);
        Assert.Equal(2, f3.Order);
        Assert.Equal(FightBilling.CoMain, f1.Billing);
        Assert.Equal(FightBilling.Main, f3.Billing);
    }

    [Fact]
    public void RemoveFight_Unknown_ReturnsFalse()
    {
        Assert.False(CreateEvent().RemoveFight(Guid.NewGuid()));
    }

    [Fact]
    public void ReplaceAthlete_OnScheduledFight_ReplacesCorner()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt);
        var substitute = Guid.NewGuid();

        evt.ReplaceAthlete(fight.Id, Corner.Blue, substitute);

        Assert.Equal(substitute, fight.BlueCornerAthleteId);
    }

    [Fact]
    public void ReplaceAthlete_WithAthleteAlreadyInCard_Throws()
    {
        var evt = CreateEvent();
        var other = AddFight(evt);
        var fight = AddFight(evt);

        Assert.Throws<InvalidOperationException>(() =>
            evt.ReplaceAthlete(fight.Id, Corner.Red, other.RedCornerAthleteId));
    }

    [Fact]
    public void ReplaceAthlete_WithFightsOwnOtherCornerAthlete_Throws()
    {
        var evt = CreateEvent();
        var fight = AddFight(evt);

        // The other corner's athlete is already in the card, so the aggregate rejects it.
        Assert.Throws<InvalidOperationException>(() =>
            evt.ReplaceAthlete(fight.Id, Corner.Blue, fight.RedCornerAthleteId));
    }

    [Fact]
    public void HasAthlete_FindsAthleteInEitherCorner()
    {
        var evt = CreateEvent();
        var red = Guid.NewGuid();
        var blue = Guid.NewGuid();
        AddFight(evt, red: red, blue: blue);

        Assert.True(evt.HasAthlete(red));
        Assert.True(evt.HasAthlete(blue));
        Assert.False(evt.HasAthlete(Guid.NewGuid()));
    }
}
