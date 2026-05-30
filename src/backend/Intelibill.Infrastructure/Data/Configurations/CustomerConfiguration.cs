using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ShopId)
            .IsRequired();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(c => c.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.Address)
            .HasMaxLength(320);

        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.CreditLimit)
            .IsRequired()
            .HasColumnType("numeric(10,2)")
            .HasDefaultValue(0m);

        builder.HasIndex(c => c.ShopId);
        builder.HasIndex(c => new { c.ShopId, c.IsActive });
        builder.HasIndex(c => new { c.ShopId, c.PhoneNumber });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(c => c.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
