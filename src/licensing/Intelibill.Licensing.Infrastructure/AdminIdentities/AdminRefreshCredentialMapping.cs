using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminRefreshCredentialMapping : IEntityTypeConfiguration<AdminRefreshCredential>
{
    public void Configure(EntityTypeBuilder<AdminRefreshCredential> builder)
    {
        builder.ToTable("admin_refresh_credentials", table =>
        {
            table.HasCheckConstraint("ck_admin_refresh_credential_dates",
                "expires_at > issued_at AND (consumed_at IS NULL OR consumed_at >= issued_at)");
            table.HasCheckConstraint("ck_admin_refresh_credential_hash", "token_hash ~ '^[0-9A-F]{64}$'");
            table.HasCheckConstraint("ck_admin_refresh_credential_parent", "parent_id IS NULL OR parent_id <> id");
        });
        builder.Property(credential => credential.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(credential => credential.TokenHash).IsUnique();
        // One successor per credential and one root per session: a fork cannot be stored.
        builder.HasIndex(credential => credential.ParentId).IsUnique();
        builder.HasIndex(credential => credential.SessionId, "ix_admin_refresh_credentials_session_root")
            .IsUnique().HasFilter("parent_id IS NULL").HasDatabaseName("ix_admin_refresh_credentials_session_root");
        builder.HasIndex(credential => credential.SessionId);
        builder.HasOne<AdminSession>().WithMany().HasForeignKey(credential => credential.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdminRefreshCredential>().WithOne().HasForeignKey<AdminRefreshCredential>(credential => credential.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
