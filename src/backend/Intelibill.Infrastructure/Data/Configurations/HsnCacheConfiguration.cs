using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class HsnCacheConfiguration : IEntityTypeConfiguration<HsnCache>
{
    public void Configure(EntityTypeBuilder<HsnCache> builder)
    {
        builder.ToTable("hsn_cache");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.ProductName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(h => h.HsnCodes)
            .HasColumnType("text[]")
            .IsRequired();

        builder.OwnsMany(h => h.TaxScenarios, scenarios =>
        {
            scenarios.ToJson();
            scenarios.Property(s => s.Condition).IsRequired();
            scenarios.Property(s => s.TaxPercentage).IsRequired();
        });

        builder.Property(h => h.CachedAt)
            .IsRequired();

        builder.HasIndex(h => h.ProductName);
    }
}
