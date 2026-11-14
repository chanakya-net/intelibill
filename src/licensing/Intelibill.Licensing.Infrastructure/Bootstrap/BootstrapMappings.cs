using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Bootstrap;

internal sealed class BootstrapMapping : IEntityTypeConfiguration<InitialAdminBootstrap>
{
    public void Configure(EntityTypeBuilder<InitialAdminBootstrap> builder)
    {
        builder.ToTable("initial_admin_bootstrap", table =>
        {
            table.HasCheckConstraint("ck_bootstrap_singleton", "id = 1");
            table.HasCheckConstraint("ck_bootstrap_preparation", """
                (num_nonnulls(proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at) = 0
                  OR (num_nonnulls(proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at) = 6
                      AND proof_digest ~ '^[0-9A-F]{64}$' AND prepared_at < expires_at))
                AND ((owner_id IS NULL AND consumed_at IS NULL)
                  OR (owner_id IS NOT NULL AND consumed_at IS NOT NULL AND prepared_at IS NOT NULL
                      AND prepared_at <= consumed_at AND consumed_at < expires_at))
                """);
        });
        builder.Property(state => state.Id).ValueGeneratedNever();
        builder.Property(state => state.ProofDigest).HasMaxLength(64);
        builder.Property(state => state.VerifiedEmail).HasMaxLength(254);
        builder.Property(state => state.VerificationReference).HasMaxLength(128);
        builder.Property(state => state.AdminOrigin).HasMaxLength(255);
        builder.Property(state => state.Version).IsRowVersion();
        builder.HasOne<AdminIdentity>().WithOne().HasForeignKey<InitialAdminBootstrap>(state => state.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasData(new { Id = 1 });
    }
}

internal sealed class AdminIdentityMapping : IEntityTypeConfiguration<AdminIdentity>
{
    public void Configure(EntityTypeBuilder<AdminIdentity> builder)
    {
        builder.ToTable("admin_identities", table =>
            table.HasCheckConstraint("ck_admin_mfa_date", "mfa_enrolled_at >= verified_at"));
        builder.Property(owner => owner.Email).HasMaxLength(254);
        builder.HasIndex(owner => owner.Email).IsUnique();
        builder.Property(owner => owner.PasswordHash).HasMaxLength(60);
        builder.Property(owner => owner.VerificationReference).HasMaxLength(128);
        builder.Property(owner => owner.Version).IsRowVersion();
    }
}

internal sealed class EnrollmentCredentialMapping : IEntityTypeConfiguration<BootstrapEnrollmentCredential>
{
    public void Configure(EntityTypeBuilder<BootstrapEnrollmentCredential> builder)
    {
        builder.ToTable("bootstrap_enrollment_credentials", table =>
            table.HasCheckConstraint("ck_bootstrap_enrollment_evidence",
                "digest ~ '^[0-9A-F]{64}$' AND issued_at < expires_at AND (revoked_at IS NULL OR revoked_at >= issued_at)"));
        builder.Property(credential => credential.Digest).HasMaxLength(64);
        builder.Property(credential => credential.Version).IsRowVersion();
        builder.HasIndex(credential => credential.Digest).IsUnique();
        builder.HasIndex(credential => credential.OwnerId).IsUnique().HasFilter("revoked_at IS NULL");
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(credential => credential.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProviderAuditMapping : IEntityTypeConfiguration<ProviderAuditEvent>
{
    public void Configure(EntityTypeBuilder<ProviderAuditEvent> builder)
    {
        builder.ToTable("provider_audit_events", table =>
            table.HasCheckConstraint("ck_bootstrap_audit_action", "action BETWEEN 1 AND 11"));
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(audit => audit.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminInvitation>().WithMany().HasForeignKey(audit => audit.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
