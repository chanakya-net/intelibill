using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("shops");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(160);

        builder.Property(s => s.Address)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(s => s.City)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(s => s.Pincode)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(s => s.ContactPerson)
            .HasMaxLength(120);

        builder.Property(s => s.MobileNumber)
            .HasMaxLength(32);

        builder.Property(s => s.GstNumber)
            .HasMaxLength(20);

        builder.Property(s => s.LegalName)
            .HasMaxLength(Shop.MaxLegalNameLength);

        builder.Property(s => s.StateCode)
            .HasMaxLength(2);

        builder.Property(s => s.TaxRegime)
            .IsRequired();

        builder.Property(s => s.InvoiceSeriesCode)
            .IsRequired()
            .HasMaxLength(InvoiceNumberFormat.MaxSeriesCodeLength)
            .HasDefaultValue(InvoiceNumberFormat.DefaultSeriesCode);

        builder.Property(s => s.EInvoiceStatus)
            .IsRequired();

        builder.Ignore(s => s.SellerRegistrationKey);

        builder.HasMany(s => s.Memberships)
            .WithOne(sm => sm.Shop)
            .HasForeignKey(sm => sm.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
