using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("stock_counts", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_stock_counts_quantities_non_negative", "observed_quantity >= 0 AND counted_quantity >= 0");
            tableBuilder.HasCheckConstraint("ck_stock_counts_delta", "delta = counted_quantity - observed_quantity");
            tableBuilder.HasCheckConstraint("ck_stock_counts_reason_required", "length(btrim(reason)) > 0");
            tableBuilder.HasCheckConstraint(
                "ck_stock_counts_movement_links",
                "(delta = 0 AND inventory_adjustment_id IS NULL AND stock_transaction_id IS NULL) OR (delta <> 0 AND inventory_adjustment_id IS NOT NULL AND stock_transaction_id IS NOT NULL)");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ObservedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(c => c.CountedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(c => c.Delta).HasPrecision(18, 3).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(StockCount.MaxReasonLength).IsRequired();
        builder.Property(c => c.ExpectedBatchVersion).HasColumnType("bigint").IsRequired();

        builder.HasIndex(c => new { c.ShopId, c.InventoryBatchId, c.PerformedAt });
        builder.HasIndex(c => new { c.ShopId, c.ItemId, c.PerformedAt });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(c => c.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.Id, b.ItemId, b.ShopId })
            .HasForeignKey(c => new { c.InventoryBatchId, c.ItemId, c.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryAdjustment>()
            .WithMany()
            .HasForeignKey(c => c.InventoryAdjustmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockTransaction>()
            .WithMany()
            .HasForeignKey(c => c.StockTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
