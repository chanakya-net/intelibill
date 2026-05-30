using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ShopId)
            .IsRequired();

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(i => i.Description)
            .HasMaxLength(1000);

        builder.Property(i => i.Uom)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(i => i.Barcode)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(i => i.IsActive)
            .IsRequired();

        builder.Property(i => i.CreatedBy)
            .IsRequired();

        builder.Property(i => i.UpdatedBy);

        builder.Property(i => i.HsnCode)
            .HasMaxLength(20);

        builder.Property(i => i.DefaultTaxRatePercent)
            .IsRequired()
            .HasPrecision(5, 2)
            .HasDefaultValue(0m);

        builder.Property(i => i.DefaultTaxIncluded)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(i => new { i.ShopId, i.Barcode })
            .IsUnique();

        builder.HasIndex(i => new { i.ShopId, i.Name });
        builder.HasIndex(i => new { i.ShopId, i.IsActive });
        builder.HasIndex(i => new { i.Id, i.ShopId })
            .IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(i => i.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
