using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ShopSetupPreferencesConfiguration : IEntityTypeConfiguration<ShopSetupPreferences>
{
    public void Configure(EntityTypeBuilder<ShopSetupPreferences> builder)
    {
        builder.ToTable("shop_setup_preferences", table =>
        {
            table.HasCheckConstraint("ck_shop_setup_preferences_version", "version >= 0");
            table.HasCheckConstraint("ck_shop_setup_preferences_acknowledgement",
                "(sample_receipt_acknowledged_at IS NULL) = (sample_receipt_acknowledged_by_user_id IS NULL)");
        });
        builder.HasKey(p => p.ShopId);
        builder.Property(p => p.Version).IsConcurrencyToken();
        builder.HasOne<Shop>().WithMany().HasForeignKey(p => p.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ShopMembership>().WithMany().HasPrincipalKey(m => new { m.ShopId, m.UserId })
            .HasForeignKey(p => new { p.ShopId, UserId = p.SampleReceiptAcknowledgedByUserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
