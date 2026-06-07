using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseOrderReceiptLineConfiguration : IEntityTypeConfiguration<PurchaseOrderReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceiptLine> builder)
    {
        builder.ToTable("purchase_order_receipt_lines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.PurchaseOrderReceiptId).IsRequired();
        builder.Property(l => l.PurchaseOrderLineId).IsRequired();
        builder.Property(l => l.ItemId).IsRequired();
        builder.Property(l => l.InventoryBatchId).IsRequired();
        builder.Property(l => l.StockTransactionId).IsRequired();
        builder.Property(l => l.Quantity).HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.TotalPurchaseCost).HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.UnitCost).HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.Mrp).HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.SalesPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(l => l.TaxRatePercent).HasPrecision(5, 2).IsRequired();
        builder.Property(l => l.TaxIncluded).IsRequired();
        builder.Property(l => l.PurchaseTaxIncluded).IsRequired();

        builder.HasIndex(l => l.PurchaseOrderReceiptId);
        builder.HasIndex(l => l.PurchaseOrderLineId);
        builder.HasIndex(l => l.InventoryBatchId).IsUnique();
        builder.HasIndex(l => l.StockTransactionId).IsUnique();

        builder.HasOne<PurchaseOrderLine>()
            .WithMany()
            .HasForeignKey(l => l.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasForeignKey(l => l.InventoryBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockTransaction>()
            .WithMany()
            .HasForeignKey(l => l.StockTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
