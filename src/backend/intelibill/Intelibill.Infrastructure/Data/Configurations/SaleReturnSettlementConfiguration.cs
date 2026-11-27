using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleReturnSettlementConfiguration : IEntityTypeConfiguration<SaleReturnSettlement>
{
    public void Configure(EntityTypeBuilder<SaleReturnSettlement> builder)
    {
        builder.ToTable("sale_return_settlements", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sale_return_settlements_amount_positive", "amount > 0");
            tableBuilder.HasCheckConstraint(
                "ck_sale_return_settlements_references_by_kind",
                "(kind = 'STORE_CREDIT_ISSUED' AND credit_note_id IS NOT NULL AND customer_ledger_entry_id IS NULL) OR (kind = 'RECEIVABLE_REDUCTION' AND credit_note_id IS NULL) OR (kind = 'EXTERNAL_REFUND' AND credit_note_id IS NULL AND customer_ledger_entry_id IS NULL)");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ShopId).IsRequired();
        builder.Property(s => s.SaleReturnId).IsRequired();
        builder.Property(s => s.Sequence).IsRequired();
        builder.Property(s => s.Amount).HasPrecision(18, 2).IsRequired();

        builder.Property(s => s.Kind)
            .HasConversion(k => ToProviderValue(k), value => FromProviderValue(value))
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(s => new { s.SaleReturnId, s.Sequence }).IsUnique();
        builder.HasIndex(s => new { s.ShopId, s.SaleReturnId });
        builder.HasIndex(s => s.CreditNoteId);
        builder.HasIndex(s => s.CustomerLedgerEntryId);
        builder.HasIndex(s => s.CustomerId);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(s => s.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CreditNote>()
            .WithMany()
            .HasForeignKey(s => s.CreditNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CustomerLedgerEntry>()
            .WithMany()
            .HasForeignKey(s => s.CustomerLedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(ReturnSettlementKind kind) =>
        kind switch
        {
            ReturnSettlementKind.ReceivableReduction => "RECEIVABLE_REDUCTION",
            ReturnSettlementKind.ExternalRefund => "EXTERNAL_REFUND",
            ReturnSettlementKind.StoreCreditIssued => "STORE_CREDIT_ISSUED",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown return settlement kind."),
        };

    private static ReturnSettlementKind FromProviderValue(string value) =>
        value switch
        {
            "RECEIVABLE_REDUCTION" => ReturnSettlementKind.ReceivableReduction,
            "EXTERNAL_REFUND" => ReturnSettlementKind.ExternalRefund,
            "STORE_CREDIT_ISSUED" => ReturnSettlementKind.StoreCreditIssued,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown return settlement kind."),
        };
}
