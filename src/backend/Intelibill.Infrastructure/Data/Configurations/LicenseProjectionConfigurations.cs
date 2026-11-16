using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class LicenseGrantEvidenceConfiguration : IEntityTypeConfiguration<LicenseGrantEvidence>
{
    public void Configure(EntityTypeBuilder<LicenseGrantEvidence> builder)
    {
        builder.ToTable("license_grant_evidence");
        builder.HasKey(row => row.EventId);
        builder.Property(row => row.EventId).ValueGeneratedNever();
        builder.HasIndex(row => row.ShopId);
        builder.Property(row => row.KeyId).HasMaxLength(128);
        builder.Property(row => row.PublicKey).HasMaxLength(2048);
        builder.Property(row => row.Assertion).HasMaxLength(4096);
        builder.Property(row => row.Digest).HasMaxLength(64);
    }
}

internal sealed class ShopLicenseStateConfiguration : IEntityTypeConfiguration<ShopLicenseState>
{
    public void Configure(EntityTypeBuilder<ShopLicenseState> builder)
    {
        builder.ToTable("shop_license_states");
        builder.HasKey(row => row.ShopId);
        builder.Property(row => row.ShopId).ValueGeneratedNever();
        builder.HasOne<Shop>().WithOne().HasForeignKey<ShopLicenseState>(row => row.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LicenseGrantEvidence>().WithMany().HasForeignKey(row => row.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountLicenseRestriction>().WithMany().HasForeignKey(row => row.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountLicenseRestrictionConfiguration : IEntityTypeConfiguration<AccountLicenseRestriction>
{
    public void Configure(EntityTypeBuilder<AccountLicenseRestriction> builder)
    {
        builder.ToTable("account_license_restrictions");
        builder.HasKey(row => row.AccountId);
        builder.Property(row => row.AccountId).ValueGeneratedNever();
        builder.HasOne<LicenseGrantEvidence>().WithMany().HasForeignKey(row => row.EvidenceId).OnDelete(DeleteBehavior.Restrict);
    }
}
