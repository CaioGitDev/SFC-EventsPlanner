using System.Text.RegularExpressions;
using Sfc.Domain.Athletes;
using Sfc.Domain.Common;

namespace Sfc.Domain.Events;

/// <summary>
/// One-to-one result of a <see cref="Fight"/>. Created and mutated only through the
/// <see cref="Event"/> aggregate, which validates against the fight's context.
/// Effects on athlete records are expressed as <see cref="RecordDelta"/>s so that
/// corrections and deletions revert by applying the negated delta.
/// </summary>
public partial class FightResult : IOrganizationScoped
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid FightId { get; private set; }

    /// <summary>Null for Draw and No Contest.</summary>
    public Guid? WinnerAthleteId { get; private set; }

    public FightResultMethod Method { get; private set; }

    /// <summary>Only for KO/TKO (required) and disqualification (optional).</summary>
    public int? Round { get; private set; }

    /// <summary>Time within the round, "m:ss". Only for KO/TKO/disqualification.</summary>
    public string? Time { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private FightResult()
    {
    }

    internal FightResult(Fight fight, Guid? winnerAthleteId, FightResultMethod method,
        int? round, string? time)
    {
        Validate(fight, winnerAthleteId, method, round, time);

        Id = Guid.NewGuid();
        OrganizationId = fight.OrganizationId;
        FightId = fight.Id;
        WinnerAthleteId = winnerAthleteId;
        Method = method;
        Round = round;
        Time = NormalizeTime(time);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    internal void Update(Fight fight, Guid? winnerAthleteId, FightResultMethod method,
        int? round, string? time)
    {
        Validate(fight, winnerAthleteId, method, round, time);

        WinnerAthleteId = winnerAthleteId;
        Method = method;
        Round = round;
        Time = NormalizeTime(time);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Effect on each corner's record per the method matrix (sfc-contexto).</summary>
    public (RecordDelta Red, RecordDelta Blue) GetDeltas(Guid redAthleteId, Guid blueAthleteId)
        => Method switch
        {
            FightResultMethod.NoContest => (RecordDelta.Zero, RecordDelta.Zero),
            FightResultMethod.Draw => (new(0, 0, 1, 0), new(0, 0, 1, 0)),
            _ => WinnerAthleteId == redAthleteId
                ? (WinnerDelta(), LoserDelta())
                : (LoserDelta(), WinnerDelta()),
        };

    internal static bool HasWinner(FightResultMethod method)
        => method is not (FightResultMethod.Draw or FightResultMethod.NoContest);

    private RecordDelta WinnerDelta()
        => Method is FightResultMethod.Ko or FightResultMethod.Tko
            ? new(1, 0, 0, 1)
            : new(1, 0, 0, 0);

    private static RecordDelta LoserDelta() => new(0, 1, 0, 0);

    private static void Validate(Fight fight, Guid? winnerAthleteId, FightResultMethod method,
        int? round, string? time)
    {
        if (HasWinner(method))
        {
            if (winnerAthleteId is null)
                throw new ArgumentException("This method requires a winner.", nameof(winnerAthleteId));
            if (winnerAthleteId != fight.RedCornerAthleteId && winnerAthleteId != fight.BlueCornerAthleteId)
                throw new ArgumentException("Winner must be one of the fight's corners.", nameof(winnerAthleteId));
        }
        else if (winnerAthleteId is not null)
        {
            throw new ArgumentException("Draw and no contest cannot have a winner.", nameof(winnerAthleteId));
        }

        var allowsRoundTime = method is FightResultMethod.Ko or FightResultMethod.Tko
            or FightResultMethod.Disqualification;
        if (!allowsRoundTime && (round is not null || !string.IsNullOrWhiteSpace(time)))
            throw new ArgumentException("This method does not take a round or time.", nameof(round));

        if (method is FightResultMethod.Ko or FightResultMethod.Tko && round is null)
            throw new ArgumentException("KO/TKO require the round.", nameof(round));

        if (round is not null && (round < 1 || round > fight.Rounds))
            throw new ArgumentException("Round must be within the fight's rounds.", nameof(round));

        if (!string.IsNullOrWhiteSpace(time))
        {
            var trimmed = time.Trim();
            if (!TimeFormat().IsMatch(trimmed))
                throw new ArgumentException("Time must be in m:ss format.", nameof(time));

            var parts = trimmed.Split(':');
            var totalSeconds = int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
            if (totalSeconds > fight.RoundDurationMinutes * 60)
                throw new ArgumentException("Time cannot exceed the round duration.", nameof(time));
        }
    }

    private static string? NormalizeTime(string? time)
        => string.IsNullOrWhiteSpace(time) ? null : time.Trim();

    [GeneratedRegex(@"^\d{1,2}:[0-5]\d$")]
    private static partial Regex TimeFormat();
}
