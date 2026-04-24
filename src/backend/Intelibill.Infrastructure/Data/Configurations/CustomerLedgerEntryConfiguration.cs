using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class CustomerLedgerEntryConfiguration : IEntityTypeConfiguration<CustomerLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CustomerLedgerEntry> builder)
    {
        builder.ToTable("customer_ledger_entries", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_customer_ledger_entries_amount_positive", "amount > 0");
            tableBuilder.HasCheckConstraint(
                "ck_customer_ledger_entries_sale_by_type",
                "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type = 'PAYMENT_RECEIVED' AND sale_id IS NULL))");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ShopId)
            .IsRequired();

        builder.Property(e => e.CustomerId)
            .IsRequired();

        builder.Property(e => e.SaleId);

        builder.Property(e => e.EntryType)
            .HasConversion(e => ToProviderValue(e), value => FromProviderValue(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(e => e.EntryDate)
            .IsRequired();

        builder.Property(e => e.Notes)
            .HasMaxLength(255);

        builder.Property(e => e.CreatedBy)
            .IsRequired();

        builder.HasIndex(e => new { e.ShopId, e.CustomerId, e.EntryDate });
        builder.HasIndex(e => new { e.ShopId, e.SaleId, e.EntryType });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(e => e.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(e => e.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(CustomerLedgerEntryType entryType)
    {
        return entryType switch
        {
            CustomerLedgerEntryType.SaleDue => "SALE_DUE",
            CustomerLedgerEntryType.PaymentReceived => "PAYMENT_RECEIVED",
            _ => throw new ArgumentOutOfRangeException(nameof(entryType), entryType, null)
        };
    }

    private static CustomerLedgerEntryType FromProviderValue(string value)
    {
        return value switch
        {
            "SALE_DUE" => CustomerLedgerEntryType.SaleDue,
            "PAYMENT_RECEIVED" => CustomerLedgerEntryType.PaymentReceived,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}
