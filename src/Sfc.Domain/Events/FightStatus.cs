namespace Sfc.Domain.Events;

/// <summary>Completed/Cancelled/NoContest only gain behavior with results (prompt 03).</summary>
public enum FightStatus
{
    Scheduled,
    Completed,
    Cancelled,
    NoContest,
}
