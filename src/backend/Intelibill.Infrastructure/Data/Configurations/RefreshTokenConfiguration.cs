using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);

        // Hex SHA-256. Optional only while rows written before hashing remain; the contract
        // migration that drops the legacy raw-token column makes this required.
        builder.Property(rt => rt.TokenHash)
            .HasMaxLength(64)
            .IsRequired(false);

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique();

        // A credential renews exactly once, so it can be the parent of at most one successor. The
        // rotation already serialises on row locks; this is the backstop if that ever regresses.
        builder.HasIndex(rt => rt.ParentTokenId)
            .IsUnique()
            .HasFilter("parent_token_id IS NOT NULL");

        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.RevocationReason)
            .HasMaxLength(64);

        builder.HasOne(rt => rt.User)
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Nullable for the rows that predate sessions; a null session is refused on rotation
        // rather than adopted, so no device is ever invented from a legacy credential.
        builder.HasOne<AuthSession>()
            .WithMany()
            .HasForeignKey(rt => rt.AuthSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rt => rt.AuthSessionId);
    }
}
