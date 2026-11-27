using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleCancellationLineConfiguration : IEntityTypeConfiguration<SaleCancellationLine>
{
    public void Configure(EntityTypeBuilder<SaleCancellationLine> builder)
    {
        builder.ToTable("sale_cancellation_lines", table => table.HasCheckConstraint("ck_sale_cancellation_lines_amounts",
            "quantity > 0 AND original_cost_price >= 0 AND revenue_amount >= 0 AND recorded_cost_amount >= 0"));
        builder.Property(x => x.QuarantinedQuantity).HasPrecision(18, 3).HasDefaultValue(0m);
        builder.Property(x => x.DispositionReasonCode).HasMaxLength(80);
        builder.HasOne<BatchDispositionChange>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId }).HasForeignKey(x => new { x.QuarantineChangeId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SaleCancellationId, x.SaleItemId }).IsUnique();
        builder.HasIndex(x => x.StockTransactionId).IsUnique();
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        // Keep the exact original unit cost for legacy raw-product readers.
        builder.Property(x => x.OriginalCostPrice).HasPrecision(28, 10);
        builder.Property(x => x.RevenueAmount).HasPrecision(18, 2);
        builder.Property(x => x.RecordedCostAmount).HasPrecision(18, 2);
        builder.Property(x => x.Description).HasMaxLength(180);
        builder.Property(x => x.Uqc).HasMaxLength(80);
        builder.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleItem>().WithMany().HasPrincipalKey(x => new { x.Id, x.SaleId, x.ShopId })
            .HasForeignKey(x => new { x.SaleItemId, x.SaleId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Item>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryBatch>().WithMany().HasPrincipalKey(x => new { x.Id, x.ItemId, x.ShopId })
            .HasForeignKey(x => new { x.InventoryBatchId, x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockTransaction>().WithMany().HasPrincipalKey(x => new { x.Id, x.ItemId, x.InventoryBatchId, x.ShopId })
            .HasForeignKey(x => new { x.StockTransactionId, x.ItemId, x.InventoryBatchId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
    }
}
