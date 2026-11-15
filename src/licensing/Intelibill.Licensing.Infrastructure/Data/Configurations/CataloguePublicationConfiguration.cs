using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class CataloguePreparationConfiguration : IEntityTypeConfiguration<CataloguePreparation>
{
    public void Configure(EntityTypeBuilder<CataloguePreparation> builder)
    {
        builder.ToTable("catalogue_preparations", table => table.HasCheckConstraint("ck_catalogue_preparation",
            "expected_version > 0 AND snapshot_hash ~ '^[A-F0-9]{64}$' AND char_length(reason) BETWEEN 1 AND 500 " +
            "AND (plan_id IS NULL) = (trial_policy_version_id IS NULL)"));
        builder.HasKey(row => new { row.AdminId, row.OperationKey });
        builder.Property(row => row.Reason).HasMaxLength(500);
        builder.Property(row => row.ConfigurationRevision).HasMaxLength(80);
        builder.Property(row => row.SnapshotHash).HasMaxLength(64);
    }
}

internal sealed class CataloguePublicationConfiguration : IEntityTypeConfiguration<CataloguePublication>
{
    public void Configure(EntityTypeBuilder<CataloguePublication> builder)
    {
        builder.ToTable("catalogue_publications");
        builder.HasKey(row => row.VersionId);
        builder.Property(row => row.VersionId).ValueGeneratedNever();
        builder.HasIndex(row => new { row.AdminId, row.OperationKey }).IsUnique();
        builder.HasOne<CataloguePreparation>().WithMany().HasForeignKey(row => new { row.AdminId, row.OperationKey })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
