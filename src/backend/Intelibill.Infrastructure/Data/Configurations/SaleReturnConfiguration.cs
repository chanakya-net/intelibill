using Intelibill.Infrastructure.Data.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("sale_returns", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sale_returns_refund_amounts_non_negative", "total_refund_amount >= 0 AND due_reduction_amount >= 0 AND payout_amount >= 0 AND total_taxable_amount >= 0 AND total_tax_amount >= 0");
            tableBuilder.HasCheckConstraint("ck_sale_returns_refund_split", "due_reduction_amount + payout_amount = total_refund_amount");
            tableBuilder.HasCheckConstraint(
                "ck_sale_returns_accounting_balanced",
                "accounting_status IS DISTINCT FROM 'BALANCED' OR (accounting_policy_version IS NOT NULL AND adjustment_taxable_amount IS NOT NULL AND adjustment_tax_amount IS NOT NULL AND adjustment_non_tax_amount IS NOT NULL AND adjustment_taxable_amount >= 0 AND adjustment_tax_amount >= 0 AND adjustment_non_tax_amount >= 0 AND adjustment_taxable_amount + adjustment_tax_amount + adjustment_non_tax_amount = total_refund_amount)");
            tableBuilder.HasCheckConstraint(
                "ck_sale_returns_accounting_review_reason",
                "accounting_status IS DISTINCT FROM 'NEEDS_REVIEW' OR accounting_review_reason IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_sale_returns_void_audit", "(is_voided = false AND voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR (is_voided = true AND voided_at IS NOT NULL AND voided_by IS NOT NULL AND void_reason IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.RefundTender).HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<TenderLine>(), InvoiceDocumentJson.Comparer<TenderLine>());
        builder.Property(r => r.VoidSettlementEvidence).HasColumnType("jsonb")
            .HasConversion(InvoiceDocumentJson.Converter<TillSettlementEvidence>(), InvoiceDocumentJson.Comparer<TillSettlementEvidence>());


        builder.Property(r => r.ShopId)
            .IsRequired();

        builder.Property(r => r.SaleId)
            .IsRequired();

        builder.Property(r => r.ReturnNumber)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(r => r.ProcessedAt)
            .IsRequired();

        builder.Property(r => r.BusinessDate)
            .IsRequired();

        builder.Property(r => r.BusinessTimeZoneId)
            .HasColumnName("business_time_zone")
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.VoidedBusinessDate);

        builder.Property(r => r.ProcessedBy)
            .IsRequired();

        builder.Property(r => r.Notes)
            .HasMaxLength(1000);

        builder.Property(r => r.TotalRefundAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.DueReductionAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.PayoutAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.PayoutDestination)
            .HasConversion<int?>()
            .HasColumnName("payout_destination");

        builder.Property(r => r.TotalTaxableAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.TotalTaxAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.CustomerBalanceBefore)
            .HasPrecision(18, 2);

        builder.Property(r => r.CustomerBalanceAfter)
            .HasPrecision(18, 2);

        builder.Property(r => r.IsVoided)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.VoidReason)
            .HasMaxLength(500);

        builder.Property(r => r.AccountingStatus)
            .HasConversion(s => ToProviderValue(s), value => FromProviderValue(value))
            .HasMaxLength(20);

        builder.Property(r => r.AccountingPolicyVersion)
            .HasMaxLength(ReturnAccountingSnapshot.MaxPolicyVersionLength);

        builder.Property(r => r.AccountingReviewReason)
            .HasMaxLength(200);

        builder.Property(r => r.AdjustmentTaxableAmount)
            .HasPrecision(18, 2);

        builder.Property(r => r.AdjustmentTaxAmount)
            .HasPrecision(18, 2);

        builder.Property(r => r.AdjustmentNonTaxAmount)
            .HasPrecision(18, 2);

        builder.Ignore(r => r.OriginalReturnedGross);

        builder.HasIndex(r => new { r.ShopId, r.ReturnNumber })
            .IsUnique();

        builder.HasIndex(r => new { r.ShopId, r.SaleId });
        builder.HasIndex(r => new { r.ShopId, r.ProcessedAt });
        builder.HasIndex(r => new { r.ShopId, r.IsVoided });
        builder.HasIndex(r => new { r.ShopId, r.VoidedAt });
        builder.HasIndex(r => new { r.ShopId, r.BusinessDate });
        builder.HasIndex(r => new { r.ShopId, r.VoidedBusinessDate });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(r => r.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.SaleReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Settlements)
            .WithOne()
            .HasForeignKey(s => s.SaleReturnId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static string? ToProviderValue(ReturnAccountingStatus? status) =>
        status switch
        {
            null => null,
            ReturnAccountingStatus.Balanced => "BALANCED",
            ReturnAccountingStatus.NeedsReview => "NEEDS_REVIEW",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown return accounting status."),
        };

    private static ReturnAccountingStatus? FromProviderValue(string? value) =>
        value switch
        {
            null => null,
            "BALANCED" => ReturnAccountingStatus.Balanced,
            "NEEDS_REVIEW" => ReturnAccountingStatus.NeedsReview,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown return accounting status."),
        };
}
