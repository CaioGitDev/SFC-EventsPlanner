using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sfc.Domain.Athletes;
using Sfc.Domain.Clubs;
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
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Athlete> Athletes => Set<Athlete>();

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

        builder.Entity<Club>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.LogoUrl).HasMaxLength(500);
            entity.Property(c => c.City).HasMaxLength(100);
            entity.Property(c => c.Country).HasMaxLength(100);
            entity.Property(c => c.ContactEmail).HasMaxLength(200);
            entity.Property(c => c.ContactPhone).HasMaxLength(50);
            entity.OwnsMany(c => c.Coaches, coaches => coaches.ToJson());
            entity.HasIndex(c => c.OrganizationId);
        });

        builder.Entity<Athlete>(entity =>
        {
            entity.Property(a => a.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.LastName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Nickname).HasMaxLength(100);
            entity.Property(a => a.Slug).HasMaxLength(200).IsRequired();
            entity.Property(a => a.PhotoUrl).HasMaxLength(500);
            entity.Property(a => a.Nationality).HasMaxLength(100).IsRequired();
            entity.Property(a => a.CoachName).HasMaxLength(200);
            entity.Property(a => a.WeightClass).HasMaxLength(50);
            entity.Property(a => a.WeightKg).HasPrecision(5, 2);
            entity.Property(a => a.Discipline).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Notes).HasMaxLength(2000);
            entity.HasIndex(a => new { a.OrganizationId, a.Slug }).IsUnique();
            entity.HasOne(a => a.Club)
                .WithMany()
                .HasForeignKey(a => a.ClubId)
                .OnDelete(DeleteBehavior.Restrict);
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
