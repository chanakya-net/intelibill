using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ReconciliationIssueConfiguration : IEntityTypeConfiguration<ReconciliationIssue>
{
    public void Configure(EntityTypeBuilder<ReconciliationIssue> builder)
    {
        builder.ToTable("reconciliation_issues");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ShopId)
            .IsRequired();

        builder.Property(i => i.ClientSaleId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(i => i.DeviceId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(i => i.IssueType)
            .IsRequired();

        builder.Property(i => i.PrintedQuantity)
            .HasPrecision(18, 3);

        builder.Property(i => i.AvailableQuantity)
            .HasPrecision(18, 3);

        builder.Property(i => i.ConsumedQuantity)
            .HasPrecision(18, 3);

        builder.Property(i => i.Code)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(i => i.Message)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(i => i.IsResolved)
            .IsRequired();

        builder.Property(i => i.CreatedBy)
            .IsRequired();

        builder.HasIndex(i => new { i.ShopId, i.IsResolved });
        builder.HasIndex(i => new { i.ShopId, i.ClientSaleId, i.DeviceId });
        builder.HasIndex(i => i.SaleId);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(i => i.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(i => i.ItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<InventoryBatch>()
            .WithMany()
            .HasForeignKey(i => i.InventoryBatchId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
