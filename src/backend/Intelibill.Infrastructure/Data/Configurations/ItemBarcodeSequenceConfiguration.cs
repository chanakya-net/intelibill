using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ItemBarcodeSequenceConfiguration : IEntityTypeConfiguration<ItemBarcodeSequence>
{
    public void Configure(EntityTypeBuilder<ItemBarcodeSequence> builder)
    {
        builder.ToTable("item_barcode_sequences");

        builder.HasKey(sequence => sequence.Id);

        builder.Property(sequence => sequence.ShopId)
            .IsRequired();

        builder.Property(sequence => sequence.NextNumber)
            .IsRequired();

        builder.Property(sequence => sequence.Prefix)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(sequence => sequence.ShopId)
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(sequence => sequence.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
