using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SupplierLedgerEntryConfiguration : IEntityTypeConfiguration<SupplierLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplierLedgerEntry> builder)
    {
        builder.ToTable("supplier_ledger_entries", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_supplier_ledger_entries_amount_non_zero", "amount <> 0");
            tableBuilder.HasCheckConstraint(
                "ck_supplier_ledger_entries_batch_by_type",
                "((entry_type = 'GOODS_RECEIVED' AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL))");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ShopId)
            .IsRequired();

        builder.Property(e => e.SupplierId)
            .IsRequired();

        builder.Property(e => e.BatchId);

        builder.Property(e => e.EntryType)
            .HasConversion(e => ToProviderValue(e), value => FromProviderValue(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(e => e.EntryDate)
            .IsRequired();

        builder.Property(e => e.Notes)
            .HasMaxLength(255);

        builder.Property(e => e.CreatedBy)
            .IsRequired();

        builder.HasIndex(e => new { e.ShopId, e.SupplierId, e.EntryDate });
        builder.HasIndex(e => new { e.ShopId, e.BatchId, e.EntryType });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(e => e.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(SupplierLedgerEntryType entryType)
    {
        return entryType switch
        {
            SupplierLedgerEntryType.GoodsReceived => "GOODS_RECEIVED",
            SupplierLedgerEntryType.PaymentMade => "PAYMENT_MADE",
            SupplierLedgerEntryType.RecordAdjusted => "RECORD_ADJUSTED",
            SupplierLedgerEntryType.Reversal => "REVERSAL"
        };
    }

    private static SupplierLedgerEntryType FromProviderValue(string value)
    {
        return value switch
        {
            "GOODS_RECEIVED" => SupplierLedgerEntryType.GoodsReceived,
            "PAYMENT_MADE" => SupplierLedgerEntryType.PaymentMade,
            "RECORD_ADJUSTED" => SupplierLedgerEntryType.RecordAdjusted,
            "REVERSAL" => SupplierLedgerEntryType.Reversal
        };
    }
}
