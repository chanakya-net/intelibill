using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_items");

        builder.HasKey(si => si.Id);

        builder.Property(si => si.SaleId)
            .IsRequired();

        builder.Property(si => si.ShopId)
            .IsRequired();

        builder.Property(si => si.ItemId)
            .IsRequired();

        builder.Property(si => si.InventoryBatchId)
            .IsRequired();

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
    }
}
