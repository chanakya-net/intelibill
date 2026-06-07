using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseOrderReceiptConfiguration : IEntityTypeConfiguration<PurchaseOrderReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceipt> builder)
    {
        builder.ToTable("purchase_order_receipts");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ShopId).IsRequired();
        builder.Property(r => r.PurchaseOrderId).IsRequired();
        builder.Property(r => r.ReceiptNumber).HasMaxLength(32).IsRequired();
        builder.Property(r => r.ReceivedAt).IsRequired();
        builder.Property(r => r.ReferenceNumber).HasMaxLength(100);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.CreatedBy).IsRequired();

        builder.HasIndex(r => r.ShopId);
        builder.HasIndex(r => r.PurchaseOrderId);
        builder.HasIndex(r => new { r.ShopId, r.ReceiptNumber }).IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PurchaseOrder>()
            .WithMany(po => po.Receipts)
            .HasForeignKey(r => r.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.PurchaseOrderReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
