using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminRecoveryMapping : IEntityTypeConfiguration<AdminRecovery>
{
    public void Configure(EntityTypeBuilder<AdminRecovery> builder)
    {
        builder.ToTable("admin_recoveries", table => table.HasCheckConstraint("ck_admin_recovery_evidence", """
            (kind = 1 AND redeemed_at = started_at AND num_nonnulls(reviewer_id, reason, verification,
                evidence_reference, grant_digest, grant_expires_at, retired_at) = 0)
            OR (kind = 2 AND num_nonnulls(reviewer_id, reason, verification, evidence_reference,
                    grant_digest, grant_expires_at) = 6
                AND reviewer_id <> admin_id AND reason BETWEEN 1 AND 3 AND verification BETWEEN 1 AND 3
                AND evidence_reference ~ '^[A-Za-z0-9][A-Za-z0-9._:#/-]{2,63}$'
                AND grant_digest ~ '^[0-9A-F]{64}$' AND started_at < grant_expires_at
                AND (redeemed_at IS NULL OR retired_at IS NULL)
                AND (redeemed_at IS NULL OR redeemed_at >= started_at)
                AND (retired_at IS NULL OR retired_at >= started_at))
            """));
        builder.Property(recovery => recovery.Id).ValueGeneratedNever();
        builder.Property(recovery => recovery.EvidenceReference).HasMaxLength(64);
        builder.Property(recovery => recovery.GrantDigest).HasMaxLength(64);
        builder.Property(recovery => recovery.Version).IsRowVersion();
        builder.HasIndex(recovery => recovery.GrantDigest).IsUnique();
        builder.HasIndex(recovery => recovery.AdminId);
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(recovery => recovery.AdminId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(recovery => recovery.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
