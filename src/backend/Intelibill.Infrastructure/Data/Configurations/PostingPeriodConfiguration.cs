using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PostingPeriodConfiguration : IEntityTypeConfiguration<PostingPeriod>
{
    public void Configure(EntityTypeBuilder<PostingPeriod> builder)
    {
        builder.ToTable("posting_periods", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_posting_periods_range", "end_date >= start_date");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.ShopId).IsRequired();
        builder.Property(p => p.StartDate).IsRequired();
        builder.Property(p => p.EndDate).IsRequired();
        builder.Property(p => p.State).IsRequired();

        builder.HasIndex(p => new { p.ShopId, p.StartDate }).IsUnique();

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(p => p.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
