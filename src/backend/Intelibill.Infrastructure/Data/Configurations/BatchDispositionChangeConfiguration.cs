using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Intelibill.Infrastructure.Data.Configurations;
internal sealed class BatchDispositionChangeConfiguration : IEntityTypeConfiguration<BatchDispositionChange>
{
    public void Configure(EntityTypeBuilder<BatchDispositionChange> b)
    {
        b.ToTable("batch_disposition_changes", t =>
        {
            t.HasCheckConstraint("ck_batch_disposition_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_batch_disposition_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
            t.HasCheckConstraint("ck_batch_disposition_source", "source_kind IN ('SaleReturn','SaleCancellation') AND source_id <> '00000000-0000-0000-0000-000000000000' AND source_line_id <> '00000000-0000-0000-0000-000000000000'");
            t.HasCheckConstraint("ck_batch_disposition_action", "(root_change_id IS NULL AND action = 'Quarantine' AND stock_movement_id IS NOT NULL AND inventory_adjustment_id IS NULL) OR (root_change_id IS NOT NULL AND ((action = 'Release' AND stock_movement_id IS NULL AND inventory_adjustment_id IS NULL) OR (action = 'Dispose' AND stock_movement_id IS NOT NULL AND inventory_adjustment_id IS NOT NULL) OR (action = 'Reverse' AND stock_movement_id IS NOT NULL AND inventory_adjustment_id IS NULL)))");
            t.HasCheckConstraint("ck_batch_disposition_cause", "reason_code IN ('Expiry.Expired','Expiry.RequiredDateMissing')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Action).HasMaxLength(20);
        b.Property(x => x.SourceKind).HasMaxLength(30);
        b.Property(x => x.ReasonCode).HasMaxLength(80);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.PolicyJson).HasColumnType("jsonb");
        b.HasAlternateKey(x => new { x.Id, x.ShopId });
        b.HasAlternateKey(x => new { x.Id, x.InventoryBatchId, x.ItemId, x.ShopId });
        b.HasIndex(x => new { x.ShopId, x.InventoryBatchId, x.RecordedAt });
        b.HasIndex(x => new { x.ShopId, x.SourceKind, x.SourceId, x.SourceLineId }).IsUnique().HasFilter("root_change_id IS NULL");
        b.HasOne<InventoryBatch>().WithMany().HasPrincipalKey(x => new { x.Id, x.ItemId, x.ShopId })
            .HasForeignKey(x => new { x.InventoryBatchId, x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BatchDispositionChange>().WithMany().HasPrincipalKey(x => new { x.Id, x.InventoryBatchId, x.ItemId, x.ShopId })
            .HasForeignKey(x => new { x.RootChangeId, x.InventoryBatchId, x.ItemId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockTransaction>().WithMany().HasForeignKey(x => x.StockMovementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryAdjustment>().WithMany().HasForeignKey(x => x.InventoryAdjustmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
