using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class LicenseHistoryReviewObservationConfiguration : IEntityTypeConfiguration<LicenseHistoryReviewObservation>
{
    public void Configure(EntityTypeBuilder<LicenseHistoryReviewObservation> builder)
    {
        builder.ToTable("license_history_review_observations");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.ShopId).IsRequired();
        builder.Property(row => row.CaseId).IsRequired();
        builder.Property(row => row.ObservedAt).IsRequired();
        builder.Property(row => row.ReasonCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Subreason).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ComparedField).HasMaxLength(64);
        builder.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();

        builder.HasIndex(row => new { row.ShopId, row.CaseId, row.Fingerprint }).IsUnique();
        builder.HasIndex(row => new { row.ShopId, row.CaseId, row.ObservedAt, row.Id });
        builder.HasIndex(row => new { row.ShopId, row.OfflineSubmissionId });

        builder.HasOne<Shop>().WithMany()
            .HasForeignKey(row => row.ShopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReconciliationIssue>().WithMany()
            .HasForeignKey(row => new { row.CaseId, row.ShopId })
            .HasPrincipalKey(issue => new { issue.Id, issue.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OfflineSubmission>().WithMany()
            .HasForeignKey(row => new { row.OfflineSubmissionId, row.ShopId })
            .HasPrincipalKey(submission => new { submission.Id, submission.ShopId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OfflineSubmission>().WithMany()
            .HasForeignKey(row => new { row.CompetingSubmissionId, row.ShopId })
            .HasPrincipalKey(submission => new { submission.Id, submission.ShopId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
