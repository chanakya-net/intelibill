using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ShopTillSettingConfiguration : IEntityTypeConfiguration<ShopTillSetting>
{
    public void Configure(EntityTypeBuilder<ShopTillSetting> b)
    {
        b.ToTable("shop_till_settings", t => t.HasCheckConstraint("ck_shop_till_settings_version", "version >= 0"));
        b.HasKey(x => x.ShopId);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShopTillSettingEventConfiguration : IEntityTypeConfiguration<ShopTillSettingEvent>
{
    public void Configure(EntityTypeBuilder<ShopTillSettingEvent> b)
    {
        b.ToTable("shop_till_setting_events", t => {
            t.HasCheckConstraint("ck_shop_till_setting_events_version", "version > 0 AND previous_enabled <> enabled");
            t.HasCheckConstraint("ck_shop_till_setting_events_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
        });
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShopId, x.Version }).IsUnique();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.ActorUserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
