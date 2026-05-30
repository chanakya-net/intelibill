using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleReturnItemConfiguration : IEntityTypeConfiguration<SaleReturnItem>
{
    public void Configure(EntityTypeBuilder<SaleReturnItem> builder)
    {
        builder.ToTable("sale_return_items", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sale_return_items_quantity_positive", "quantity > 0");
            tableBuilder.HasCheckConstraint("ck_sale_return_items_tax_rate_range", "original_tax_rate_percent >= 0 AND original_tax_rate_percent <= 100");
            tableBuilder.HasCheckConstraint("ck_sale_return_items_amounts_non_negative", "original_cost_price >= 0 AND original_sales_price >= 0 AND max_refund_amount >= 0 AND approved_refund_amount >= 0 AND taxable_amount >= 0 AND tax_amount >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_sale_return_items_refund_cap",
                "approved_refund_amount <= max_refund_amount OR notes IS NOT NULL");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.SaleReturnId)
            .IsRequired();

        builder.Property(i => i.ShopId)
            .IsRequired();

        builder.Property(i => i.SaleId)
            .IsRequired();

        builder.Property(i => i.SaleItemId)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.Condition)
            .HasConversion(c => ToProviderValue(c), value => FromProviderValue(value))
            .HasMaxLength(20);

        builder.Property(i => i.OriginalCostPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.OriginalSalesPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.OriginalTaxRatePercent)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(i => i.OriginalIsPriceIncludingTax)
            .IsRequired();

        builder.Property(i => i.MaxRefundAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.ApprovedRefundAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.TaxableAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.TaxAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(i => new { i.ShopId, i.SaleItemId });
        builder.HasIndex(i => new { i.ShopId, i.SaleId });
        builder.HasIndex(i => new { i.ShopId, i.Condition });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(i => i.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SaleItem>()
            .WithMany()
            .HasForeignKey(i => i.SaleItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string? ToProviderValue(SaleReturnCondition? condition)
    {
        if (!condition.HasValue)
            return null;

        return condition switch
        {
            SaleReturnCondition.Restockable => "RESTOCKABLE",
            SaleReturnCondition.Wastage => "WASTAGE",
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
        };
    }

    private static SaleReturnCondition? FromProviderValue(string? value)
    {
        if (value is null)
            return null;

        return value switch
        {
            "RESTOCKABLE" => SaleReturnCondition.Restockable,
            "WASTAGE" => SaleReturnCondition.Wastage,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}
