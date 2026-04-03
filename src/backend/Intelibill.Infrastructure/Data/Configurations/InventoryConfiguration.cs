using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_inventory_quantity_non_negative", "quantity >= 0");
            tableBuilder.HasCheckConstraint("ck_inventory_reorder_non_negative", "reorder_level >= 0");
            tableBuilder.HasCheckConstraint("ck_inventory_max_non_negative", "max_level >= 0");
            tableBuilder.HasCheckConstraint("ck_inventory_reorder_lte_max", "reorder_level <= max_level");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ShopId)
            .IsRequired();

        builder.Property(i => i.ItemId)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.ReorderLevel)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.MaxLevel)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.LastUpdatedAt)
            .IsRequired();

        builder.Property(i => i.CreatedBy)
            .IsRequired();

        builder.Property(i => i.UpdatedBy);

        builder.HasIndex(i => new { i.ShopId, i.ItemId })
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(i => i.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Item)
            .WithOne(i => i.Inventory)
            .HasPrincipalKey<Item>(i => new { i.Id, i.ShopId })
            .HasForeignKey<Inventory>(i => new { i.ItemId, i.ShopId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}