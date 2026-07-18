namespace Sfc.Domain.Athletes;

/// <summary>Per-athlete effect of one fight result on the record (see sfc-contexto matrix).</summary>
public record RecordDelta(int Wins, int Losses, int Draws, int Kos)
{
    public static readonly RecordDelta Zero = new(0, 0, 0, 0);

    public RecordDelta Negate() => new(-Wins, -Losses, -Draws, -Kos);
}
