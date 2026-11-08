using Intelibill.Infrastructure.Data.Json;

using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PaymentReceiptConfiguration : IEntityTypeConfiguration<PaymentReceipt>
{
    public void Configure(EntityTypeBuilder<PaymentReceipt> builder)
    {
        builder.ToTable("payment_receipts", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payment_receipts_amount_positive", "amount > 0");
            tableBuilder.HasCheckConstraint(
                "ck_payment_receipts_expense_supplier_only",
                "expense_id IS NULL OR party_type = 'SUPPLIER'");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.PartyType)
            .HasConversion(v => PaymentEnumMapping.ToProvider(v), v => PaymentEnumMapping.PartyTypeFrom(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Method)
            .HasConversion(v => PaymentEnumMapping.ToProvider(v), v => PaymentEnumMapping.MethodFrom(v))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.ExternalReference).HasMaxLength(PaymentReceipt.MaxExternalReferenceLength);
        builder.Property(r => r.Notes).HasMaxLength(PaymentReceipt.MaxNotesLength);
        builder.Property(r => r.BusinessDate).IsRequired();
        builder.Property(r => r.ReceivedAt).IsRequired();
        builder.Property(r => r.LedgerEntryId).IsRequired();
        builder.Property(r => r.IsLegacy).IsRequired();
        builder.Property(r => r.CreatedBy).IsRequired();

        builder.HasIndex(r => new { r.ShopId, r.PartyType, r.PartyId, r.BusinessDate });

        // One receipt per payment ledger entry: the backfill and a new write can never both own it.
        builder.HasIndex(r => new { r.PartyType, r.LedgerEntryId }).IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(r => r.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Expense>()
            .WithMany()
            .HasForeignKey(r => r.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations", tableBuilder =>
            tableBuilder.HasCheckConstraint("ck_payment_allocations_amount_positive", "amount > 0"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.TargetType)
            .HasConversion(v => PaymentEnumMapping.ToProvider(v), v => PaymentEnumMapping.TargetTypeFrom(v))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(a => a.CreatedBy).IsRequired();

        builder.HasIndex(a => new { a.ShopId, a.ReceiptId });
        builder.HasIndex(a => new { a.ShopId, a.TargetType, a.TargetId });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(a => a.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PaymentReceipt>()
            .WithMany()
            .HasForeignKey(a => a.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentReversalConfiguration : IEntityTypeConfiguration<PaymentReversal>
{
    public void Configure(EntityTypeBuilder<PaymentReversal> builder)
    {
        builder.ToTable("payment_reversals", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payment_reversals_amount_positive", "amount > 0");
            tableBuilder.HasCheckConstraint("ck_payment_reversals_reason_required", "length(btrim(reason)) >= 3");
        });

        builder.HasKey(r => r.Id);
        builder.Property(e => e.SettlementEvidence).HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<TillSettlementEvidence>(), InvoiceDocumentJson.Comparer<TillSettlementEvidence>());

        builder.Property(r => r.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(PaymentReversal.MaxReasonLength).IsRequired();
        builder.Property(r => r.LedgerEntryId).IsRequired();
        builder.Property(r => r.BusinessDate).IsRequired();
        builder.Property(r => r.ReversedBy).IsRequired();

        // A receipt is reversed at most once; a second attempt fails here even if it slips past the lock.
        builder.HasIndex(r => r.ReceiptId).IsUnique();
        builder.HasIndex(r => r.ShopId);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PaymentReceipt>()
            .WithMany()
            .HasForeignKey(r => r.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class PaymentEnumMapping
{
    public static string ToProvider(PaymentPartyType value) => value switch
    {
        PaymentPartyType.Customer => "CUSTOMER",
        PaymentPartyType.Supplier => "SUPPLIER",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PaymentPartyType PartyTypeFrom(string value) => value switch
    {
        "CUSTOMER" => PaymentPartyType.Customer,
        "SUPPLIER" => PaymentPartyType.Supplier,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToProvider(ReceiptTenderMethod value) => value switch
    {
        ReceiptTenderMethod.UnknownRecordedMethod => "UNKNOWN_RECORDED_METHOD",
        ReceiptTenderMethod.Cash => "CASH",
        ReceiptTenderMethod.Upi => "UPI",
        ReceiptTenderMethod.Card => "CARD",
        ReceiptTenderMethod.BankTransfer => "BANK_TRANSFER",
        ReceiptTenderMethod.Cheque => "CHEQUE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ReceiptTenderMethod MethodFrom(string value) => value switch
    {
        "UNKNOWN_RECORDED_METHOD" => ReceiptTenderMethod.UnknownRecordedMethod,
        "CASH" => ReceiptTenderMethod.Cash,
        "UPI" => ReceiptTenderMethod.Upi,
        "CARD" => ReceiptTenderMethod.Card,
        "BANK_TRANSFER" => ReceiptTenderMethod.BankTransfer,
        "CHEQUE" => ReceiptTenderMethod.Cheque,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToProvider(PaymentAllocationTargetType value) => value switch
    {
        PaymentAllocationTargetType.CustomerInvoice => "CUSTOMER_INVOICE",
        PaymentAllocationTargetType.SupplierReceipt => "SUPPLIER_RECEIPT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PaymentAllocationTargetType TargetTypeFrom(string value) => value switch
    {
        "CUSTOMER_INVOICE" => PaymentAllocationTargetType.CustomerInvoice,
        "SUPPLIER_RECEIPT" => PaymentAllocationTargetType.SupplierReceipt,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
