using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.OwnerUserId)
            .IsRequired();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(s => s.IsSystem)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.ContactPersonName)
            .HasMaxLength(120);

        builder.Property(s => s.ContactPersonPhone)
            .HasMaxLength(32);

        builder.Property(s => s.Address)
            .HasMaxLength(320);

        builder.Property(s => s.City)
            .HasMaxLength(120);

        builder.Property(s => s.State)
            .HasMaxLength(120);

        builder.Property(s => s.Pin)
            .HasMaxLength(16);

        builder.Property(s => s.IsActive)
            .IsRequired();

        builder.Property(s => s.IsPreferred)
            .IsRequired();

        builder.HasIndex(s => s.OwnerUserId);
        builder.HasIndex(s => new { s.OwnerUserId, s.IsActive });
        builder.HasIndex(s => new { s.OwnerUserId, s.IsSystem })
            .IsUnique()
            .HasFilter("is_system = true");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}