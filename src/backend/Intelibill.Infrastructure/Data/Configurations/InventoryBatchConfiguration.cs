using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class InventoryBatchConfiguration : IEntityTypeConfiguration<InventoryBatch>
{
    public void Configure(EntityTypeBuilder<InventoryBatch> builder)
    {
        builder.ToTable("inventory_batches", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_inventory_batches_quantity_non_negative", "quantity >= 0");
            tableBuilder.HasCheckConstraint("ck_inventory_batches_tax_rate_range", "tax_rate_percent >= 0 AND tax_rate_percent <= 100");
            tableBuilder.HasCheckConstraint("ck_inventory_batches_sales_lte_mrp", "sales_price <= mrp");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.ShopId)
            .IsRequired();

        builder.Property(b => b.ItemId)
            .IsRequired();

        builder.Property(b => b.BatchNumber)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(b => b.Quantity)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(b => b.OriginalQuantity)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(b => b.CostPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.Mrp)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.SalesPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(b => b.TaxRatePercent)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(b => b.TaxIncluded)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(b => b.IsVoided)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(b => b.SupplierId);

        builder.Property(b => b.CreatedBy)
            .IsRequired();

        builder.Property(b => b.UpdatedBy);

        builder.HasIndex(b => new { b.ShopId, b.ItemId, b.BatchNumber })
            .IsUnique()
            .HasFilter("is_voided = false");

        builder.HasIndex(b => new { b.ShopId, b.ExpiryDate });
        builder.HasIndex(b => new { b.ShopId, b.SupplierId });
        builder.HasIndex(b => new { b.Id, b.ItemId, b.ShopId })
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(b => b.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Item)
            .WithMany(i => i.Batches)
            .HasPrincipalKey(i => new { i.Id, i.ShopId })
            .HasForeignKey(b => new { b.ItemId, b.ShopId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(b => b.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}