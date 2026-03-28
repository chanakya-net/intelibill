using Intelibill.Domain.Entities;
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

        builder.HasMany(s => s.Memberships)
            .WithOne(sm => sm.Shop)
            .HasForeignKey(sm => sm.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}