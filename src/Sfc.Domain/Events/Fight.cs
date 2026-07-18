using Sfc.Domain.Athletes;
using Sfc.Domain.Common;

namespace Sfc.Domain.Events;

/// <summary>
/// Created and mutated only through the <see cref="Event"/> aggregate, which owns
/// order contiguity, billing derivation, and athlete uniqueness in the card.
/// </summary>
public class Fight : IOrganizationScoped
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid EventId { get; private set; }

    /// <summary>1 opens the event; the highest order is the main event.</summary>
    public int Order { get; private set; }

    /// <summary>Derived from position by the aggregate — never set manually.</summary>
    public FightBilling Billing { get; private set; }

    public Discipline Discipline { get; private set; }
    public int Rounds { get; private set; }
    public int RoundDurationMinutes { get; private set; }
    public string? WeightClass { get; private set; }
    public decimal? CatchweightKg { get; private set; }
    public bool IsTitleFight { get; private set; }
    public bool IsAmateur { get; private set; }
    public Guid RedCornerAthleteId { get; private set; }
    public Athlete? RedCornerAthlete { get; private set; }
    public Guid BlueCornerAthleteId { get; private set; }
    public Athlete? BlueCornerAthlete { get; private set; }
    public FightStatus Status { get; private set; }

    /// <summary>1:1 result; null while scheduled, cancelled, or no contest without a ruling.</summary>
    public FightResult? Result { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Fight()
    {
    }

    internal Fight(Guid organizationId, Guid eventId, int order,
        Guid redCornerAthleteId, Guid blueCornerAthleteId, Discipline discipline,
        int rounds, int roundDurationMinutes, string? weightClass, decimal? catchweightKg,
        bool isTitleFight, bool isAmateur)
    {
        if (redCornerAthleteId == Guid.Empty)
            throw new ArgumentException("Red corner athlete is required.", nameof(redCornerAthleteId));
        if (blueCornerAthleteId == Guid.Empty)
            throw new ArgumentException("Blue corner athlete is required.", nameof(blueCornerAthleteId));
        if (redCornerAthleteId == blueCornerAthleteId)
            throw new ArgumentException("An athlete cannot be in both corners.", nameof(blueCornerAthleteId));
        if (rounds is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(rounds));
        if (roundDurationMinutes is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(roundDurationMinutes));
        if (catchweightKg is <= 0)
            throw new ArgumentException("Catchweight must be positive.", nameof(catchweightKg));

        var hasWeightClass = !string.IsNullOrWhiteSpace(weightClass);
        if (hasWeightClass == catchweightKg.HasValue)
            throw new ArgumentException(
                "Exactly one of weight class or catchweight is required.", nameof(weightClass));

        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        EventId = eventId;
        Order = order;
        Billing = FightBilling.Card;
        Discipline = discipline;
        Rounds = rounds;
        RoundDurationMinutes = roundDurationMinutes;
        WeightClass = hasWeightClass ? weightClass!.Trim() : null;
        CatchweightKg = catchweightKg;
        IsTitleFight = isTitleFight;
        IsAmateur = isAmateur;
        RedCornerAthleteId = redCornerAthleteId;
        BlueCornerAthleteId = blueCornerAthleteId;
        Status = FightStatus.Scheduled;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    internal void SetOrder(int order)
    {
        Order = order;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void SetBilling(FightBilling billing)
    {
        Billing = billing;
        UpdatedAt = DateTime.UtcNow;
    }

    internal FightResult RecordResult(Guid? winnerAthleteId, FightResultMethod method,
        int? round, string? time)
    {
        if (Status != FightStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled fights can receive a result.");

        Result = new FightResult(this, winnerAthleteId, method, round, time);
        Status = StatusFor(method);
        UpdatedAt = DateTime.UtcNow;
        return Result;
    }

    internal FightResult ChangeResult(Guid? winnerAthleteId, FightResultMethod method,
        int? round, string? time)
    {
        if (Result is null)
            throw new InvalidOperationException("This fight has no result to change.");

        Result.Update(this, winnerAthleteId, method, round, time);
        Status = StatusFor(method);
        UpdatedAt = DateTime.UtcNow;
        return Result;
    }

    internal void DeleteResult()
    {
        if (Result is null)
            throw new InvalidOperationException("This fight has no result to delete.");

        Result = null;
        Status = FightStatus.Scheduled;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void Cancel()
    {
        if (Status != FightStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled fights can be cancelled.");

        Status = FightStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void MarkNoContest()
    {
        if (Status != FightStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled fights can be marked no contest.");

        Status = FightStatus.NoContest;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void Reinstate()
    {
        if (Result is not null || Status is not (FightStatus.Cancelled or FightStatus.NoContest))
            throw new InvalidOperationException(
                "Only cancelled or no-contest fights without a result can be reinstated.");

        Status = FightStatus.Scheduled;
        UpdatedAt = DateTime.UtcNow;
    }

    private static FightStatus StatusFor(FightResultMethod method)
        => method == FightResultMethod.NoContest ? FightStatus.NoContest : FightStatus.Completed;

    internal void ReplaceCorner(Corner corner, Guid athleteId)
    {
        if (Status != FightStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled fights can have athletes replaced.");
        if (athleteId == Guid.Empty)
            throw new ArgumentException("Athlete is required.", nameof(athleteId));
        if (corner == Corner.Red && athleteId == BlueCornerAthleteId)
            throw new ArgumentException("An athlete cannot be in both corners.", nameof(athleteId));
        if (corner == Corner.Blue && athleteId == RedCornerAthleteId)
            throw new ArgumentException("An athlete cannot be in both corners.", nameof(athleteId));

        if (corner == Corner.Red)
        {
            RedCornerAthleteId = athleteId;
            RedCornerAthlete = null;
        }
        else
        {
            BlueCornerAthleteId = athleteId;
            BlueCornerAthlete = null;
        }

        UpdatedAt = DateTime.UtcNow;
    }
}
