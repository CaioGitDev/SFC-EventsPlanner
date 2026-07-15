namespace Sfc.Domain.Events;

/// <summary>Up = lower Order (earlier in the night); Down = higher Order (towards the main event).</summary>
public enum MoveDirection
{
    Up,
    Down,
}
