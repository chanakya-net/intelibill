using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminInvitationMapping : IEntityTypeConfiguration<AdminInvitation>
{
    public void Configure(EntityTypeBuilder<AdminInvitation> builder)
    {
        builder.ToTable("admin_invitations", table =>
        {
            table.HasCheckConstraint("ck_admin_invitation_evidence", """
                token_digest ~ '^[0-9A-F]{64}$' AND issued_at < expires_at
                AND failed_attempts BETWEEN 0 AND 5
                AND (revoked_at IS NULL OR revoked_at >= issued_at)
                """);
            table.HasCheckConstraint("ck_admin_invitation_acceptance", """
                (accepted_at IS NULL AND accepted_admin_id IS NULL)
                OR (accepted_at IS NOT NULL AND accepted_admin_id IS NOT NULL AND revoked_at IS NULL
                    AND accepted_at >= issued_at AND accepted_at < expires_at)
                """);
        });
        builder.Property(invitation => invitation.Email).HasMaxLength(254);
        builder.Property(invitation => invitation.TokenDigest).HasMaxLength(64);
        builder.Property(invitation => invitation.Version).IsRowVersion();
        builder.HasIndex(invitation => invitation.TokenDigest).IsUnique();
        builder.HasIndex(invitation => invitation.Email).IsUnique()
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL")
            .HasDatabaseName("ix_admin_invitations_open_email");
        builder.HasIndex(invitation => invitation.AcceptedAdminId).IsUnique();
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(invitation => invitation.InvitedBy)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminIdentity>().WithOne().HasForeignKey<AdminInvitation>(invitation => invitation.AcceptedAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
