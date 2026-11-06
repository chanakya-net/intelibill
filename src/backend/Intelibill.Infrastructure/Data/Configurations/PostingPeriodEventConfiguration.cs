using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class PostingPeriodEventConfiguration : IEntityTypeConfiguration<PostingPeriodEvent>
{
    public void Configure(EntityTypeBuilder<PostingPeriodEvent> builder)
    {
        builder.ToTable("posting_period_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ShopId).IsRequired();
        builder.Property(e => e.PostingPeriodId).IsRequired();
        builder.Property(e => e.Action).IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(PostingPeriod.MaxReasonLength).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();

        builder.HasIndex(e => new { e.ShopId, e.PostingPeriodId, e.OccurredAt });

        builder.HasOne<PostingPeriod>()
            .WithMany()
            .HasForeignKey(e => e.PostingPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Shop>()
            .WithMany()
            .HasForeignKey(e => e.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
