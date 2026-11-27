using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;

internal sealed class AdminTotpMapping : IEntityTypeConfiguration<AdminTotp>
{
    public void Configure(EntityTypeBuilder<AdminTotp> builder)
    {
        builder.ToTable("admin_totp", table =>
        {
            table.HasCheckConstraint("ck_admin_totp_secret", "octet_length(encrypted_secret) = 48 AND last_accepted_step >= 0");
            table.HasCheckConstraint("ck_admin_totp_attempts", """
                (attempts = 0 AND attempt_window_ends_at IS NULL)
                OR (attempts BETWEEN 1 AND 5 AND attempt_window_ends_at IS NOT NULL)
                """);
        });
        builder.HasKey(totp => totp.AdminId);
        builder.Property(totp => totp.KeyId).HasMaxLength(32);
        builder.Property(totp => totp.RecoveryKeyId).HasMaxLength(32);
        builder.HasOne<AdminIdentity>().WithOne().HasForeignKey<AdminTotp>(totp => totp.AdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdminRecoveryCodeMapping : IEntityTypeConfiguration<AdminRecoveryCode>
{
    public void Configure(EntityTypeBuilder<AdminRecoveryCode> builder)
    {
        builder.ToTable("admin_recovery_codes", table =>
            table.HasCheckConstraint("ck_admin_recovery_digest", "digest ~ '^[0-9A-F]{64}$'"));
        builder.HasKey(code => new { code.AdminId, code.Digest });
        builder.Property(code => code.Digest).HasMaxLength(64);
        builder.HasOne<AdminTotp>().WithMany().HasForeignKey(code => code.AdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
