using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_items", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_sale_items_line_type_refs",
                "((line_type = 'GOODS' AND item_id IS NOT NULL AND inventory_batch_id IS NOT NULL AND service_id IS NULL) OR (line_type = 'SERVICE' AND service_id IS NOT NULL AND item_id IS NULL AND inventory_batch_id IS NULL))");
            tableBuilder.HasCheckConstraint(
                "ck_sale_items_line_snapshots_required",
                "line_name IS NOT NULL AND length(btrim(line_name)) > 0 AND line_code IS NOT NULL AND length(btrim(line_code)) > 0");
        });

        builder.HasKey(si => si.Id);

        builder.Property(si => si.SaleId)
            .IsRequired();

        builder.Property(si => si.ShopId)
            .IsRequired();

        builder.Property(si => si.LineType)
            .HasConversion(v => ToProviderValue(v), v => FromProviderValue(v))
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(si => si.ItemId)
            .IsRequired(false);

        builder.Property(si => si.InventoryBatchId)
            .IsRequired(false);

        builder.Property(si => si.ServiceId);

        builder.Property(si => si.LineName)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(si => si.LineCode)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(si => si.Quantity)
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(si => si.CostPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.SalesPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.Mrp)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.TaxRatePercent)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(si => si.IsPriceIncludingTax)
            .IsRequired();

        builder.Property(si => si.HasPriceMismatch)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(si => si.OriginalSalesPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.FinalSalesPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.PreTaxAmountBeforeDiscount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.ItemDiscountAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.SaleDiscountAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.TaxableAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.TaxAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.HsnCode)
            .HasMaxLength(20);

        builder.Property(si => si.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(si => si.ConfiguredBatchRuleId);

        builder.Property(si => si.ConfiguredBatchRulePercentage)
            .HasPrecision(5, 2);

        builder.Property(si => si.ItemDiscountOverrideType)
            .IsRequired();

        builder.Property(si => si.ItemDiscountOverrideValue)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.HasIndex(si => si.ShopId);
        builder.HasIndex(si => new { si.ShopId, si.ItemId });
        builder.HasIndex(si => new { si.ShopId, si.ServiceId });
        builder.HasIndex(si => new { si.ShopId, si.ConfiguredBatchRuleId });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(si => si.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(si => si.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasForeignKey(si => si.InventoryBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(si => si.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ToProviderValue(SaleLineType lineType)
    {
        return lineType switch
        {
            SaleLineType.Goods => "GOODS",
            SaleLineType.Service => "SERVICE",
            _ => throw new ArgumentOutOfRangeException(nameof(lineType), lineType, null)
        };
    }

    private static SaleLineType FromProviderValue(string value)
    {
        return value switch
        {
            "GOODS" => SaleLineType.Goods,
            "SERVICE" => SaleLineType.Service,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }
}
