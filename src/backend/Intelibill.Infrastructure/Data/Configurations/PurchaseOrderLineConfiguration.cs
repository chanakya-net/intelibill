using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.PurchaseOrderId)
            .IsRequired();

        builder.Property(l => l.ItemId)
            .IsRequired();

        builder.Property(l => l.Description)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(l => l.ExpectedQuantity)
            .IsRequired();

        builder.Property(l => l.UnitCost)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Ignore(l => l.LineTotal);

        builder.HasIndex(l => l.PurchaseOrderId);
        builder.HasIndex(l => l.ItemId);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(l => l.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
