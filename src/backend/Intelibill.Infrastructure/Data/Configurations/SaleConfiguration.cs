using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ShopId)
            .IsRequired();

        builder.Property(s => s.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(s => s.CustomerId);

        builder.Property(s => s.CustomerName)
            .HasMaxLength(180);

        builder.Property(s => s.CustomerPhone)
            .HasMaxLength(32);

        builder.Property(s => s.PaymentMethod)
            .IsRequired();

        builder.Property(s => s.SoldAt)
            .IsRequired();

        builder.Property(s => s.PaidAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.DueAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.TotalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.TotalTaxAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.HasIndex(s => new { s.ShopId, s.InvoiceNumber })
            .IsUnique();

        builder.HasIndex(s => s.ShopId);
        builder.HasIndex(s => new { s.ShopId, s.SoldAt });
        builder.HasIndex(s => new { s.ShopId, s.CustomerId });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(s => s.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey(si => si.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
