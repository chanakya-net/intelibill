using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class OfflineLicenseGrantConfiguration : IEntityTypeConfiguration<OfflineLicenseGrant>
{
    public void Configure(EntityTypeBuilder<OfflineLicenseGrant> builder)
    {
        builder.ToTable("offline_license_grants");
        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id).ValueGeneratedNever();
        builder.Property(grant => grant.DeviceId).HasMaxLength(120).IsRequired();
        builder.Property(grant => grant.KeyId).HasMaxLength(128).IsRequired();
        builder.Property(grant => grant.SignedGrant).HasMaxLength(8192).IsRequired();

        // The permit's reference carries the shop, so a permit can never point at another tenant's grant.
        builder.HasAlternateKey(grant => new { grant.Id, grant.ShopId });
        // One original grant per lease, and the tenant predicate's index.
        builder.HasIndex(grant => new { grant.ShopId, grant.LeaseId }).IsUnique();

        builder.HasOne<InvoiceLease>().WithMany()
            .HasForeignKey(grant => new { grant.LeaseId, grant.ShopId })
            .HasPrincipalKey(lease => new { lease.Id, lease.ShopId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shop>().WithMany().HasForeignKey(grant => grant.ShopId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OfflineLicensePermitConfiguration : IEntityTypeConfiguration<OfflineLicensePermit>
{
    public void Configure(EntityTypeBuilder<OfflineLicensePermit> builder)
    {
        builder.ToTable("offline_license_permits");
        builder.HasKey(permit => permit.Id);
        builder.Property(permit => permit.Id).ValueGeneratedNever();
        builder.Property(permit => permit.DeviceId).HasMaxLength(120).IsRequired();
        builder.Property(permit => permit.LimitingReason).HasMaxLength(32).IsRequired();
        builder.Property(permit => permit.KeyId).HasMaxLength(128).IsRequired();
        builder.Property(permit => permit.SignedPermit).HasMaxLength(8192).IsRequired();

        // One completion per server snapshot: a second writer loses the insert and reads the first.
        builder.HasIndex(permit => new { permit.ShopId, permit.SnapshotId }).IsUnique();
        builder.HasIndex(permit => new { permit.ShopId, permit.LeaseId, permit.ValidUntil });

        builder.HasOne<OfflineLicenseGrant>().WithMany()
            .HasForeignKey(permit => new { permit.GrantId, permit.ShopId })
            .HasPrincipalKey(grant => new { grant.Id, grant.ShopId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shop>().WithMany().HasForeignKey(permit => permit.ShopId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OfflineLicenseVerificationKeyConfiguration : IEntityTypeConfiguration<OfflineLicenseVerificationKey>
{
    public void Configure(EntityTypeBuilder<OfflineLicenseVerificationKey> builder)
    {
        builder.ToTable("offline_license_verification_keys");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.KeyId).HasMaxLength(128).IsRequired();
        builder.Property(key => key.PublicKey).IsRequired();
        builder.Property(key => key.Fingerprint).HasMaxLength(64).IsRequired();

        // A key identifier names one public key for the shop, for ever.
        builder.HasIndex(key => new { key.ShopId, key.KeyId }).IsUnique();
        builder.HasOne<Shop>().WithMany().HasForeignKey(key => key.ShopId).OnDelete(DeleteBehavior.Restrict);
    }
}
