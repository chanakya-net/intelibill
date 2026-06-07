using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.HasKey(po => po.Id);

        builder.Property(po => po.ShopId)
            .IsRequired();

        builder.Property(po => po.SupplierId);

        builder.Property(po => po.PurchaseOrderNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(po => po.OrderDate);

        builder.Property(po => po.ExpectedDeliveryDate);

        builder.Property(po => po.SupplierReferenceNumber)
            .HasMaxLength(100);

        builder.Property(po => po.Notes)
            .HasMaxLength(1000);

        builder.Property(po => po.SupplierName)
            .HasMaxLength(200);

        builder.Property(po => po.SupplierReference)
            .HasMaxLength(120);

        builder.Property(po => po.Status)
            .IsRequired();

        builder.HasIndex(po => po.ShopId);
        builder.HasIndex(po => new { po.ShopId, po.PurchaseOrderNumber })
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(po => po.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(po => po.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(po => po.Lines)
            .WithOne()
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
