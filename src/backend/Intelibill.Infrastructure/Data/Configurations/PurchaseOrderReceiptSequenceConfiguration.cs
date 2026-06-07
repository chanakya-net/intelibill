using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PurchaseOrderReceiptSequenceConfiguration : IEntityTypeConfiguration<PurchaseOrderReceiptSequence>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceiptSequence> builder)
    {
        builder.ToTable("purchase_order_receipt_sequences");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ShopId).IsRequired();
        builder.Property(s => s.Year).IsRequired();
        builder.Property(s => s.NextNumber).IsRequired();
        builder.HasIndex(s => new { s.ShopId, s.Year }).IsUnique();
    }
}
