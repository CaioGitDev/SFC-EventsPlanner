using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Common;
using Sfc.Domain.Organizations;

namespace Sfc.Infrastructure.Persistence;

public class SfcDbContext(DbContextOptions<SfcDbContext> options)
    : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    /// <summary>
    /// Tenant used by the global query filter (ADR-002). Fase 1 has exactly
    /// one organization, so this defaults to the SFC seed id.
    /// </summary>
    public Guid CurrentOrganizationId { get; set; } = SeedData.SfcOrganizationId;

    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Organization>(entity =>
        {
            entity.Property(o => o.Name).HasMaxLength(200).IsRequired();
            entity.Property(o => o.Slug).HasMaxLength(200).IsRequired();
            entity.HasIndex(o => o.Slug).IsUnique();
            entity.HasData(new
            {
                Id = SeedData.SfcOrganizationId,
                Name = "SFC",
                Slug = "sfc",
                CreatedAt = SeedData.SeedTimestamp,
                UpdatedAt = SeedData.SeedTimestamp,
            });
        });

        ApplyOrganizationQueryFilters(builder);
    }

    /// <summary>
    /// Applies the tenant global query filter to every entity implementing
    /// <see cref="IOrganizationScoped"/> (ADR-002). No entity implements it
    /// yet; the convention guarantees future entities are filtered from day one.
    /// </summary>
    private void ApplyOrganizationQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(IOrganizationScoped).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(parameter, nameof(IOrganizationScoped.OrganizationId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId))),
                parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
