using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseReceiptCostAdjustmentConfiguration : IEntityTypeConfiguration<PurchaseReceiptCostAdjustment>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptCostAdjustment> builder)
    {
        builder.ToTable("purchase_receipt_cost_adjustments", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_purchase_receipt_cost_adjustments_totals_non_negative", "prior_total_cost >= 0 AND revised_total_cost >= 0");
            tableBuilder.HasCheckConstraint("ck_purchase_receipt_cost_adjustments_delta", "delta = revised_total_cost - prior_total_cost AND delta <> 0");
            tableBuilder.HasCheckConstraint("ck_purchase_receipt_cost_adjustments_reason_required", "length(btrim(reason)) > 0");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.PriorTotalCost).HasPrecision(18, 2).IsRequired();
        builder.Property(a => a.RevisedTotalCost).HasPrecision(18, 2).IsRequired();
        builder.Property(a => a.Delta).HasPrecision(18, 2).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(PurchaseReceiptCostAdjustment.MaxReasonLength).IsRequired();

        builder.HasIndex(a => new { a.ShopId, a.PurchaseReceiptValueId, a.PerformedAt });
        builder.HasIndex(a => a.SupplierLedgerEntryId).IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(a => a.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PurchaseReceiptValue>()
            .WithMany()
            .HasForeignKey(a => a.PurchaseReceiptValueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasForeignKey(a => a.InventoryBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SupplierLedgerEntry>()
            .WithMany()
            .HasForeignKey(a => a.SupplierLedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
