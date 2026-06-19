using Intelibill.Domain.Entities;
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
            tableBuilder.HasCheckConstraint("ck_sale_returns_void_audit", "(is_voided = false AND voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR (is_voided = true AND voided_at IS NOT NULL AND voided_by IS NOT NULL AND void_reason IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ShopId)
            .IsRequired();

        builder.Property(r => r.SaleId)
            .IsRequired();

        builder.Property(r => r.ReturnNumber)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(r => r.ProcessedAt)
            .IsRequired();

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

        builder.HasIndex(r => new { r.ShopId, r.ReturnNumber })
            .IsUnique();

        builder.HasIndex(r => new { r.ShopId, r.SaleId });
        builder.HasIndex(r => new { r.ShopId, r.ProcessedAt });
        builder.HasIndex(r => new { r.ShopId, r.IsVoided });

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
    }
}
