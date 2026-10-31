using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SecurityVersion).IsRequired();
        builder.Property(s => s.ScopeVersion).IsRequired();
        builder.Property(s => s.RecoveryEpoch).IsRequired().HasMaxLength(64);
        builder.Property(s => s.LastSeenAt).IsRequired();
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.DeviceLabel).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(DeviceMetadata.UserAgentMaxLength);
        builder.Property(s => s.CreatedFromIp).HasMaxLength(DeviceMetadata.IpAddressMaxLength);
        builder.Property(s => s.RevocationReason).HasMaxLength(64);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every protected request resolves a session by id; listing a user's own devices and
        // revoking all of them are the other two access paths.
        builder.HasIndex(s => new { s.UserId, s.RevokedAt });

        // No row-level security policy here, matching users, refresh_tokens and
        // password_reset_tokens: a session is account-scoped rather than shop-scoped, and it has to
        // be readable from OnTokenValidated, which runs before the request's RLS session context
        // exists. Per-user scoping is enforced by the repository.
    }
}
