using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class CatalogueRetirementConfiguration : IEntityTypeConfiguration<CatalogueRetirement>
{
    public void Configure(EntityTypeBuilder<CatalogueRetirement> builder)
    {
        builder.ToTable("catalogue_retirements", table => table.HasCheckConstraint("ck_catalogue_retirement",
            "expected_version > 0 AND char_length(btrim(reason)) BETWEEN 1 AND 500"));
        builder.HasKey(row => row.VersionId);
        builder.Property(row => row.VersionId).ValueGeneratedNever();
        builder.Property(row => row.Reason).HasMaxLength(500);
        builder.HasIndex(row => new { row.AdminId, row.OperationKey }).IsUnique();
        builder.HasOne<CataloguePublication>().WithOne().HasForeignKey<CatalogueRetirement>(row => row.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WithdrawnCatalogueOfferConfiguration : IEntityTypeConfiguration<WithdrawnCatalogueOffer>
{
    public void Configure(EntityTypeBuilder<WithdrawnCatalogueOffer> builder)
    {
        builder.ToTable("withdrawn_catalogue_offers");
        builder.HasKey(row => row.VersionId);
        builder.Property(row => row.VersionId).ValueGeneratedNever();
        builder.HasOne<PublishedCatalogueOffer>().WithOne().HasForeignKey<WithdrawnCatalogueOffer>(row => row.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CatalogueRetirement>().WithOne().HasForeignKey<WithdrawnCatalogueOffer>(row => row.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
