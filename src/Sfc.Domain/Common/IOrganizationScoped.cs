namespace Sfc.Domain.Common;

/// <summary>
/// Marker for tenant-scoped entities (ADR-002). Every domain entity except
/// <see cref="Organizations.Organization"/> must implement this; the DbContext
/// applies a global query filter on <see cref="OrganizationId"/>.
/// </summary>
public interface IOrganizationScoped
{
    Guid OrganizationId { get; }
}
