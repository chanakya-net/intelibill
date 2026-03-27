using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ShopMembershipConfiguration : IEntityTypeConfiguration<ShopMembership>
{
    public void Configure(EntityTypeBuilder<ShopMembership> builder)
    {
        builder.ToTable("shop_memberships");

        builder.HasKey(sm => sm.Id);

        builder.Property(sm => sm.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(sm => sm.IsDefault)
            .IsRequired();

        builder.HasIndex(sm => new { sm.UserId, sm.ShopId })
            .IsUnique();

        builder.HasIndex(sm => new { sm.UserId, sm.IsDefault })
            .HasFilter("is_default = true")
            .IsUnique();

        builder.HasIndex(sm => new { sm.UserId, sm.LastUsedAt });
    }
}