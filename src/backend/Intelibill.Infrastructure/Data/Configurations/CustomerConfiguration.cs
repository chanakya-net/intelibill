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

        builder.Property(c => c.OwnerUserId)
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

        builder.HasIndex(c => c.OwnerUserId);
        builder.HasIndex(c => new { c.OwnerUserId, c.IsActive });
        builder.HasIndex(c => new { c.OwnerUserId, c.PhoneNumber });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
