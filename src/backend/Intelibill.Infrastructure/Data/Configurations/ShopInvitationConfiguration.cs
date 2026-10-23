using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ShopInvitationConfiguration : IEntityTypeConfiguration<ShopInvitation>
{
    public void Configure(EntityTypeBuilder<ShopInvitation> builder)
    {
        builder.ToTable("shop_invitations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();

        // At most one live offer per shop and address, so a resend supersedes rather than stacks
        // and two concurrent invitations cannot both be accepted.
        builder.HasIndex(x => new { x.ShopId, x.Email })
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL")
            .IsUnique();

        builder.HasIndex(x => new { x.ShopId, x.CreatedAt });
    }
}
