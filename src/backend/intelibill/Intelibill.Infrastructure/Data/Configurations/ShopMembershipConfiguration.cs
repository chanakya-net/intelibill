using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
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

        // Defaulted in the database as well, so rows written by a replica that predates this
        // column land Active rather than empty during a rolling deploy.
        builder.Property(sm => sm.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(MembershipStatus.Active)
            .IsRequired();

        builder.Property(sm => sm.AuthorityVersion)
            .IsRequired();

        builder.Property(sm => sm.IsDefault)
            .IsRequired();

        builder.HasIndex(sm => new { sm.UserId, sm.ShopId })
            .IsUnique();

        builder.HasIndex(sm => new { sm.UserId, sm.IsDefault })
            .HasFilter("is_default = true")
            .IsUnique();

        builder.HasIndex(sm => new { sm.UserId, sm.LastUsedAt });

        builder.HasIndex(sm => new { sm.ShopId, sm.Status });
    }
}
