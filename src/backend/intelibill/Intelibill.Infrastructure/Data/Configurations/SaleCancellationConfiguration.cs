using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleCancellationConfiguration : IEntityTypeConfiguration<SaleCancellation>
{
    public void Configure(EntityTypeBuilder<SaleCancellation> builder)
    {
        builder.ToTable("sale_cancellations", table =>
        {
            table.HasCheckConstraint("ck_sale_cancellations_amounts", "total_amount >= 0 AND revenue_amount >= 0 AND tax_amount = 0 AND due_reduction_amount >= 0 AND cash_refund_amount >= 0 AND total_amount = due_reduction_amount + cash_refund_amount AND revenue_amount = total_amount");
            table.HasCheckConstraint("ck_sale_cancellations_settlement", "((due_reduction_amount > 0 AND customer_ledger_entry_id IS NOT NULL) OR (due_reduction_amount = 0 AND customer_ledger_entry_id IS NULL)) AND cash_refund_confirmed = (cash_refund_amount > 0) AND goods_recovered_confirmed");
            table.HasCheckConstraint("ck_sale_cancellations_reason", "length(btrim(reason)) BETWEEN 3 AND 255 AND length(btrim(operation_key)) BETWEEN 1 AND 128");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.SaleId, x.ShopId });
        builder.HasIndex(x => x.SaleId).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.ReferenceNumber }).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.BusinessDate });
        builder.Property(x => x.OperationKey).HasMaxLength(128);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(35);
        builder.Property(x => x.Reason).HasMaxLength(255);
        builder.Property(x => x.BusinessTimeZoneId).HasMaxLength(64);
        builder.Property(x => x.PolicyVersion).HasMaxLength(40);
        builder.Property(x => x.OriginalInvoiceNumber).HasMaxLength(40);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.RevenueAmount).HasPrecision(18, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(18, 2);
        builder.Property(x => x.DueReductionAmount).HasPrecision(18, 2);
        builder.Property(x => x.CashRefundAmount).HasPrecision(18, 2);
        builder.Property<InvoiceSeller?>(nameof(SaleCancellation.Seller)).IsRequired().HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<InvoiceSeller>(), InvoiceDocumentJson.Comparer<InvoiceSeller>());
        builder.Property<InvoiceBuyer?>(nameof(SaleCancellation.Buyer)).IsRequired().HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<InvoiceBuyer>(), InvoiceDocumentJson.Comparer<InvoiceBuyer>());
        builder.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Sale>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { x.SaleId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustomerLedgerEntry>().WithMany().HasPrincipalKey(x => new { x.Id, x.ShopId })
            .HasForeignKey(x => new { x.CustomerLedgerEntryId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasPrincipalKey(x => new { x.Id, x.SaleId, x.ShopId })
            .HasForeignKey(x => new { x.SaleCancellationId, x.SaleId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
    }
}
