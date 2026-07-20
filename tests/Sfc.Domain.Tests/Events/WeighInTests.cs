using Sfc.Domain.Athletes;
using Sfc.Domain.Events;
using Xunit;

namespace Sfc.Domain.Tests.Events;

public class WeighInTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static Fight CreateFight(string? weightClass = "-72kg", decimal? catchweightKg = null)
    {
        var evt = new Event(OrgId, "SFC 12", new DateTime(2026, 9, 12, 20, 0, 0), "sfc-12");
        return evt.AddFight(Guid.NewGuid(), Guid.NewGuid(), Discipline.MuayThai,
            rounds: 3, roundDurationMinutes: 3, weightClass, catchweightKg,
            isTitleFight: false, isAmateur: false);
    }

    private static WeighIn CreateWeighIn(decimal? expected = 72m)
        => new(OrgId, Guid.NewGuid(), Guid.NewGuid(), expected);

    // --- Fight.WeightLimitKg ---

    [Fact]
    public void WeightLimitKg_Catchweight_UsesCatchweight()
    {
        var fight = CreateFight(weightClass: null, catchweightKg: 74.5m);

        Assert.Equal(74.5m, fight.WeightLimitKg);
    }

    [Theory]
    [InlineData("-72kg", 72)]
    [InlineData("57,15 kg", 57.15)]
    [InlineData("Até 63.5", 63.5)]
    public void WeightLimitKg_ParsesFirstNumberFromWeightClass(string weightClass, decimal expected)
    {
        Assert.Equal(expected, CreateFight(weightClass).WeightLimitKg);
    }

    [Fact]
    public void WeightLimitKg_UnparseableWeightClass_IsNull()
    {
        Assert.Null(CreateFight("Peso Galo").WeightLimitKg);
    }

    [Theory]
    [InlineData("+90kg")]
    [InlineData(" +100")]
    public void WeightLimitKg_OpenHeavyweightClass_IsNull(string weightClass)
    {
        // "+90kg" is a floor, not a ceiling — flagging a 95kg heavyweight as a
        // weight miss would poison the whole category with false badges.
        Assert.Null(CreateFight(weightClass).WeightLimitKg);
    }

    [Theory]
    [InlineData(19.9)]
    [InlineData(9999)]
    public void SetExpectedWeight_OutsidePlausibleRange_Throws(double kg)
    {
        Assert.Throws<ArgumentException>(() =>
            CreateWeighIn().SetExpectedWeight((decimal)kg));
    }

    [Theory]
    [InlineData(19.9)]
    [InlineData(251)]
    public void RecordOfficialWeight_OutsidePlausibleRange_Throws(double kg)
    {
        // Fat-finger guard: "710" instead of "71,0" must not be stored as valid.
        Assert.Throws<ArgumentException>(() =>
            CreateWeighIn().RecordOfficialWeight((decimal)kg, DateTime.UtcNow));
    }

    // --- WeighIn ---

    [Fact]
    public void Constructor_SetsDefaults()
    {
        var weighIn = CreateWeighIn(expected: 72m);

        Assert.Equal(72m, weighIn.ExpectedWeightKg);
        Assert.Null(weighIn.OfficialWeightKg);
        Assert.Null(weighIn.WeighedAt);
        Assert.False(weighIn.IsApproved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveExpectedWeight_Throws(double expected)
    {
        Assert.Throws<ArgumentException>(() => CreateWeighIn((decimal)expected));
    }

    [Fact]
    public void RecordOfficialWeight_SetsWeightAndTime()
    {
        var weighIn = CreateWeighIn();
        var weighedAt = new DateTime(2026, 9, 11, 18, 30, 0, DateTimeKind.Utc);

        weighIn.RecordOfficialWeight(71.8m, weighedAt);

        Assert.Equal(71.8m, weighIn.OfficialWeightKg);
        Assert.Equal(weighedAt, weighIn.WeighedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void RecordOfficialWeight_NonPositive_Throws(double kg)
    {
        Assert.Throws<ArgumentException>(() =>
            CreateWeighIn().RecordOfficialWeight((decimal)kg, DateTime.UtcNow));
    }

    [Fact]
    public void Approve_WithoutOfficialWeight_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateWeighIn().Approve());
    }

    [Fact]
    public void Approve_WhenOverweight_IsAllowed()
    {
        // The prompt is explicit: a weight miss never blocks — the call is human.
        var weighIn = CreateWeighIn(expected: 72m);
        weighIn.RecordOfficialWeight(74.2m, DateTime.UtcNow);

        weighIn.Approve();

        Assert.True(weighIn.IsApproved);
    }

    [Fact]
    public void Unapprove_ClearsApproval()
    {
        var weighIn = CreateWeighIn();
        weighIn.RecordOfficialWeight(71m, DateTime.UtcNow);
        weighIn.Approve();

        weighIn.Unapprove();

        Assert.False(weighIn.IsApproved);
    }

    [Theory]
    [InlineData(72.05, 72, true)]  // above the limit
    [InlineData(72, 72, false)]    // on the limit
    [InlineData(71.9, 72, false)]  // under
    public void IsOverweight_ComparesOfficialWeightToLimit(double official, double limit, bool expected)
    {
        var weighIn = CreateWeighIn();
        weighIn.RecordOfficialWeight((decimal)official, DateTime.UtcNow);

        Assert.Equal(expected, weighIn.IsOverweight((decimal)limit));
    }

    [Fact]
    public void IsOverweight_WithoutWeightOrLimit_IsFalse()
    {
        var withoutWeight = CreateWeighIn();
        Assert.False(withoutWeight.IsOverweight(72m));

        var withoutLimit = CreateWeighIn();
        withoutLimit.RecordOfficialWeight(90m, DateTime.UtcNow);
        Assert.False(withoutLimit.IsOverweight(null));
    }

    [Fact]
    public void SetNotes_BlankBecomesNull()
    {
        var weighIn = CreateWeighIn();

        weighIn.SetNotes("  ");

        Assert.Null(weighIn.Notes);
    }
}
