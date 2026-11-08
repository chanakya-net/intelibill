using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillCloseReviewConfiguration : IEntityTypeConfiguration<TillCloseReview>
{
    public void Configure(EntityTypeBuilder<TillCloseReview> b)
    {
        b.ToTable("till_close_reviews", t => t.HasCheckConstraint("ck_till_reviews_reason", "length(btrim(reason)) BETWEEN 1 AND 500"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShopId, x.CloseId }).IsUnique();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<TillClose>().WithMany().HasPrincipalKey(x => new { x.Id, x.SessionId, x.ShopId })
            .HasForeignKey(x => new { Id = x.CloseId, x.SessionId, x.ShopId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.ReviewedBy }).OnDelete(DeleteBehavior.Restrict);
    }
}
