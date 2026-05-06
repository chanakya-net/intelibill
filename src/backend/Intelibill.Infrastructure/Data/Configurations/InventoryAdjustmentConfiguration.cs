using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("inventory_adjustments", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_inventory_adjustments_quantity_positive", "quantity > 0");
            tableBuilder.HasCheckConstraint("ck_inventory_adjustments_cost_impact_positive", "cost_impact > 0");
            tableBuilder.HasCheckConstraint("ck_inventory_adjustments_unit_cost_non_negative", "unit_cost >= 0");
            tableBuilder.HasCheckConstraint("ck_inventory_adjustments_quantity_snapshots_non_negative", "batch_quantity_before >= 0 AND batch_quantity_after >= 0 AND inventory_quantity_before >= 0 AND inventory_quantity_after >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_inventory_adjustments_direction_reason",
                "((direction = 'Decrease' AND reason IN ('Damaged', 'Expired', 'Stolen', 'MissingLost', 'StockCountCorrection', 'OtherLoss')) OR (direction = 'Increase' AND reason IN ('FoundStock', 'StockCountCorrection', 'ReturnRestockCorrection', 'OtherGain')))");
            tableBuilder.HasCheckConstraint(
                "ck_inventory_adjustments_other_reason_notes",
                "(reason NOT IN ('OtherLoss', 'OtherGain')) OR (notes IS NOT NULL AND length(btrim(notes)) > 0)");
            tableBuilder.HasCheckConstraint(
                "ck_inventory_adjustments_quantity_snapshots",
                "((direction = 'Decrease' AND batch_quantity_before - quantity = batch_quantity_after AND inventory_quantity_before - quantity = inventory_quantity_after) OR (direction = 'Increase' AND batch_quantity_before + quantity = batch_quantity_after AND inventory_quantity_before + quantity = inventory_quantity_after))");
            tableBuilder.HasCheckConstraint(
                "ck_inventory_adjustments_void_audit",
                "(is_voided = false AND voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL AND reversal_stock_transaction_id IS NULL) OR (is_voided = true AND voided_at IS NOT NULL AND voided_by IS NOT NULL AND void_reason IS NOT NULL AND reversal_stock_transaction_id IS NOT NULL)");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ShopId)
            .IsRequired();

        builder.Property(a => a.ItemId)
            .IsRequired();

        builder.Property(a => a.InventoryBatchId)
            .IsRequired();

        builder.Property(a => a.AdjustmentNumber)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(a => a.Direction)
            .HasConversion(a => ToProviderValue(a), value => DirectionFromProviderValue(value))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasConversion(a => ToProviderValue(a), value => ReasonFromProviderValue(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.Quantity)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.UnitCost)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.CostImpact)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.BatchQuantityBefore)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.BatchQuantityAfter)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.InventoryQuantityBefore)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.InventoryQuantityAfter)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(a => a.PerformedAt)
            .IsRequired();

        builder.Property(a => a.PerformedBy)
            .IsRequired();

        builder.Property(a => a.Notes)
            .HasMaxLength(1000);

        builder.Property(a => a.IsVoided)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(a => a.VoidReason)
            .HasMaxLength(500);

        builder.Property(a => a.CreatedBy)
            .IsRequired();

        builder.Property(a => a.UpdatedBy);

        builder.HasIndex(a => new { a.ShopId, a.AdjustmentNumber })
            .IsUnique();

        builder.HasIndex(a => new { a.ShopId, a.ItemId, a.PerformedAt });
        builder.HasIndex(a => new { a.ShopId, a.InventoryBatchId, a.PerformedAt });
        builder.HasIndex(a => new { a.ShopId, a.IsVoided });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(a => a.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Item)
            .WithMany()
            .HasPrincipalKey(i => new { i.Id, i.ShopId })
            .HasForeignKey(a => new { a.ItemId, a.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.InventoryBatch)
            .WithMany()
            .HasPrincipalKey(b => new { b.Id, b.ItemId, b.ShopId })
            .HasForeignKey(a => new { a.InventoryBatchId, a.ItemId, a.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.ReversalStockTransaction)
            .WithMany()
            .HasForeignKey(a => a.ReversalStockTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(InventoryAdjustmentDirection direction)
    {
        return direction switch
        {
            InventoryAdjustmentDirection.Increase => "Increase",
            InventoryAdjustmentDirection.Decrease => "Decrease",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }

    private static InventoryAdjustmentDirection DirectionFromProviderValue(string value)
    {
        return value switch
        {
            "Increase" => InventoryAdjustmentDirection.Increase,
            "Decrease" => InventoryAdjustmentDirection.Decrease,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    private static string ToProviderValue(InventoryAdjustmentReason reason)
    {
        return reason switch
        {
            InventoryAdjustmentReason.Damaged => "Damaged",
            InventoryAdjustmentReason.Expired => "Expired",
            InventoryAdjustmentReason.Stolen => "Stolen",
            InventoryAdjustmentReason.MissingLost => "MissingLost",
            InventoryAdjustmentReason.StockCountCorrection => "StockCountCorrection",
            InventoryAdjustmentReason.OtherLoss => "OtherLoss",
            InventoryAdjustmentReason.FoundStock => "FoundStock",
            InventoryAdjustmentReason.ReturnRestockCorrection => "ReturnRestockCorrection",
            InventoryAdjustmentReason.OtherGain => "OtherGain",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
    }

    private static InventoryAdjustmentReason ReasonFromProviderValue(string value)
    {
        return value switch
        {
            "Damaged" => InventoryAdjustmentReason.Damaged,
            "Expired" => InventoryAdjustmentReason.Expired,
            "Stolen" => InventoryAdjustmentReason.Stolen,
            "MissingLost" => InventoryAdjustmentReason.MissingLost,
            "StockCountCorrection" => InventoryAdjustmentReason.StockCountCorrection,
            "OtherLoss" => InventoryAdjustmentReason.OtherLoss,
            "FoundStock" => InventoryAdjustmentReason.FoundStock,
            "ReturnRestockCorrection" => InventoryAdjustmentReason.ReturnRestockCorrection,
            "OtherGain" => InventoryAdjustmentReason.OtherGain,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}
