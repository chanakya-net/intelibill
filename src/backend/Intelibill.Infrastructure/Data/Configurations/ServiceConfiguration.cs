using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ShopId)
            .IsRequired();

        builder.Property(s => s.Code)
            .IsRequired()
            .HasMaxLength(24);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(180);

        builder.Property(s => s.Description)
            .HasMaxLength(1000);

        builder.Property(s => s.Price)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(s => s.HsnCode)
            .HasMaxLength(20);

        builder.Property(s => s.TaxRatePercent)
            .IsRequired()
            .HasPrecision(5, 2)
            .HasDefaultValue(0m);

        builder.Property(s => s.TaxIncluded)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.IsActive)
            .IsRequired();

        builder.Property(s => s.CreatedBy)
            .IsRequired();

        builder.Property(s => s.UpdatedBy);

        builder.HasIndex(s => new { s.ShopId, s.Code })
            .IsUnique();

        builder.HasIndex(s => new { s.ShopId, s.Name })
            .IsUnique();

        builder.HasIndex(s => new { s.ShopId, s.IsActive });

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(s => s.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
