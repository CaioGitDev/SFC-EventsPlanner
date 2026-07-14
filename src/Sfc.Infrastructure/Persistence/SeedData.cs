namespace Sfc.Infrastructure.Persistence;

public static class SeedData
{
    /// <summary>Fixed id of the single Fase 1 organization (ADR-002).</summary>
    public static readonly Guid SfcOrganizationId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Fixed timestamp so migrations stay deterministic.</summary>
    public static readonly DateTime SeedTimestamp = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
}
