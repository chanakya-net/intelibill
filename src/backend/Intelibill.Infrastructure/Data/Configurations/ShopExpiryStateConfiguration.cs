using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ShopExpiryStateConfiguration : IEntityTypeConfiguration<ShopExpiryState>
{
    public void Configure(EntityTypeBuilder<ShopExpiryState> builder)
    {
        builder.ToTable("shop_expiry_states", t => t.HasCheckConstraint("ck_shop_expiry_states_version", "catalog_version >= 1 AND default_policy_version >= 1 AND default_policy_version <= catalog_version"));
        builder.HasKey(x => x.ShopId);
        builder.Property(x => x.CatalogVersion).IsConcurrencyToken();
        builder.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
    }
}
