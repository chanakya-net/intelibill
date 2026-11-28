using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class NotificationEmailDeliveryConfiguration : IEntityTypeConfiguration<NotificationEmailDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationEmailDelivery> builder)
    {
        builder.ToTable("notification_email_deliveries", table =>
        {
            table.HasCheckConstraint("ck_email_source", """
                (job_id IS NOT NULL AND proof_contact_id IS NULL AND protected_proof IS NULL)
                OR (job_id IS NULL AND proof_contact_id IS NOT NULL AND protected_proof IS NOT NULL AND affiliation_request_id IS NULL)
                """);
            table.HasCheckConstraint("ck_email_state", """
                state IN ('Pending','Leased','Sending','RetryPending','SmtpAccepted','Uncertain','Suppressed','Failed')
                AND attempts BETWEEN 0 AND 5
                AND (state IN ('Leased','Sending')) = (lease_token IS NOT NULL)
                AND (lease_token IS NULL) = (lease_expires_at IS NULL)
                AND (state IN ('Pending','RetryPending')) = (next_attempt_at IS NOT NULL)
                AND (state IN ('SmtpAccepted','Uncertain','Suppressed','Failed')) = (outcome_at IS NOT NULL)
                AND (attempts = 0) = (last_attempt_at IS NULL)
                """);
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasOne<NotificationJob>().WithMany()
            .HasForeignKey(row => new { row.AccountId, row.JobId })
            .HasPrincipalKey(row => new { row.AccountId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillingNoticeContact>().WithMany()
            .HasForeignKey(row => new { row.AccountId, row.ProofContactId })
            .HasPrincipalKey(row => new { row.AccountId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.JobId).IsUnique();
        builder.HasIndex(row => row.ProofContactId).IsUnique();
        builder.HasIndex(row => row.NextAttemptAt).HasFilter("state IN ('Pending','RetryPending')");
        builder.HasIndex(row => row.LeaseExpiresAt).HasFilter("state IN ('Leased','Sending')");
    }
}
