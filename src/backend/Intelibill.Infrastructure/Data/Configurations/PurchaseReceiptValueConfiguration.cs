using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseReceiptValueConfiguration : IEntityTypeConfiguration<PurchaseReceiptValue>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptValue> builder)
    {
        builder.ToTable("purchase_receipt_values", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_purchase_receipt_values_quantity_positive", "received_quantity > 0");
            tableBuilder.HasCheckConstraint(
                "ck_purchase_receipt_values_amounts_by_status",
                "(status = 'RESOLVED' AND original_total_cost IS NOT NULL AND original_total_cost >= 0 AND effective_total_cost IS NOT NULL AND effective_total_cost >= 0) OR (status = 'LEGACY_UNRESOLVED' AND original_total_cost IS NULL AND effective_total_cost IS NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReceivedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(r => r.OriginalTotalCost).HasPrecision(18, 2);
        builder.Property(r => r.EffectiveTotalCost).HasPrecision(18, 2);
        builder.Property(r => r.SourceReference).HasMaxLength(PurchaseReceiptValue.MaxSourceReferenceLength);

        builder.Property(r => r.Source)
            .HasConversion(s => ToProviderValue(s), value => SourceFromProviderValue(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion(s => ToProviderValue(s), value => StatusFromProviderValue(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Ignore(r => r.IsResolved);

        // One receipt per physical lot.
        builder.HasIndex(r => new { r.ShopId, r.InventoryBatchId }).IsUnique();
        builder.HasIndex(r => new { r.ShopId, r.SupplierId });
        builder.HasIndex(r => r.PurchaseOrderReceiptLineId);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.Id, b.ItemId, b.ShopId })
            .HasForeignKey(r => new { r.InventoryBatchId, r.ItemId, r.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<PurchaseOrderReceiptLine>()
            .WithMany()
            .HasForeignKey(r => r.PurchaseOrderReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(PurchaseReceiptSource source) =>
        source switch
        {
            PurchaseReceiptSource.DirectInbound => "DIRECT_INBOUND",
            PurchaseReceiptSource.PurchaseOrder => "PURCHASE_ORDER",
            PurchaseReceiptSource.LegacyBackfill => "LEGACY_BACKFILL",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown receipt source."),
        };

    private static PurchaseReceiptSource SourceFromProviderValue(string value) =>
        value switch
        {
            "DIRECT_INBOUND" => PurchaseReceiptSource.DirectInbound,
            "PURCHASE_ORDER" => PurchaseReceiptSource.PurchaseOrder,
            "LEGACY_BACKFILL" => PurchaseReceiptSource.LegacyBackfill,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown receipt source."),
        };

    private static string ToProviderValue(PurchaseReceiptValueStatus status) =>
        status switch
        {
            PurchaseReceiptValueStatus.Resolved => "RESOLVED",
            PurchaseReceiptValueStatus.LegacyUnresolved => "LEGACY_UNRESOLVED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown receipt status."),
        };

    private static PurchaseReceiptValueStatus StatusFromProviderValue(string value) =>
        value switch
        {
            "RESOLVED" => PurchaseReceiptValueStatus.Resolved,
            "LEGACY_UNRESOLVED" => PurchaseReceiptValueStatus.LegacyUnresolved,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown receipt status."),
        };
}
