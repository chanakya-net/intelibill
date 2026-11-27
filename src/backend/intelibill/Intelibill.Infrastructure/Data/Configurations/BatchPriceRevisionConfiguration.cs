using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class BatchPriceRevisionConfiguration : IEntityTypeConfiguration<BatchPriceRevision>
{
    public void Configure(EntityTypeBuilder<BatchPriceRevision> builder)
    {
        builder.ToTable("batch_price_revisions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_batch_price_revisions_sales_lte_mrp", "new_sales_price <= new_mrp");
            tableBuilder.HasCheckConstraint("ck_batch_price_revisions_tax_rate_range", "new_tax_rate_percent >= 0 AND new_tax_rate_percent <= 100");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.PreviousMrp).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.NewMrp).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.PreviousSalesPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.NewSalesPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.PreviousTaxRatePercent).HasPrecision(5, 2).IsRequired();
        builder.Property(r => r.NewTaxRatePercent).HasPrecision(5, 2).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(BatchPriceRevision.MaxReasonLength);

        builder.HasIndex(r => new { r.ShopId, r.InventoryBatchId, r.PerformedAt });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.Id, b.ItemId, b.ShopId })
            .HasForeignKey(r => new { r.InventoryBatchId, r.ItemId, r.ShopId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
