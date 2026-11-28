using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class NotificationJobConfiguration : IEntityTypeConfiguration<NotificationJob>
{
    public void Configure(EntityTypeBuilder<NotificationJob> builder)
    {
        builder.ToTable("notification_jobs", table =>
        {
            table.HasCheckConstraint(
                "ck_notification_jobs_ids",
                """
                id <> '00000000-0000-0000-0000-000000000000'
                AND account_id <> '00000000-0000-0000-0000-000000000000'
                AND scope_id <> '00000000-0000-0000-0000-000000000000'
                AND source_id <> '00000000-0000-0000-0000-000000000000'
                AND (recipient_user_id IS NULL OR recipient_user_id <> '00000000-0000-0000-0000-000000000000')
                AND (contact_id IS NULL OR contact_id <> '00000000-0000-0000-0000-000000000000')
                """);
            table.HasCheckConstraint(
                "ck_notification_jobs_scope",
                """
                (scope_kind = 'Account' AND scope_id = account_id AND audience IN ('CommercialBillingRole', 'CommercialContact'))
                OR (scope_kind = 'Shop' AND audience IN ('ShopOwnerOperational', 'ShopStaffOperational'))
                """);
            table.HasCheckConstraint(
                "ck_notification_jobs_purpose",
                "purpose IN ('TrialReminder', 'PaidRenewalReminder', 'GraceNotice')");
            table.HasCheckConstraint(
                "ck_notification_jobs_source",
                "source_kind IN ('Trial', 'Paid', 'Complimentary') AND length(btrim(source_revision)) BETWEEN 1 AND 128");
            table.HasCheckConstraint(
                "ck_notification_jobs_threshold",
                """
                threshold IN (
                    'TrialMinus3Days', 'TrialMinus1Day',
                    'PaidMinus7Days', 'PaidMinus1Day',
                    'GraceStart', 'GraceMinus1Day', 'GraceExpiry')
                """);
            table.HasCheckConstraint(
                "ck_notification_jobs_recipient_key",
                "length(btrim(recipient_key)) BETWEEN 1 AND 128");
            table.HasCheckConstraint(
                "ck_notification_jobs_dates",
                "effective_deadline_at >= due_at AND time_zone = 'Asia/Kolkata'");
            table.HasCheckConstraint(
                "ck_notification_jobs_locale",
                "locale IN ('en-IN', 'hi-IN', 'ta-IN', 'te-IN', 'bn-IN', 'ml-IN', 'kn-IN', 'mr-IN', 'gu-IN')");
            table.HasCheckConstraint(
                "ck_notification_jobs_channels",
                """
                cardinality(allowed_channels) >= 1
                AND allowed_channels <@ ARRAY['InApp', 'Email']::text[]
                AND (audience <> 'ShopStaffOperational' OR allowed_channels = ARRAY['InApp']::text[])
                AND (audience <> 'CommercialContact' OR allowed_channels = ARRAY['Email']::text[])
                AND (NOT ('Email' = ANY(allowed_channels)) OR (protected_destination IS NOT NULL AND destination_hint IS NOT NULL))
                """);
            table.HasCheckConstraint("ck_notification_jobs_status", "status = 'Scheduled'");
            table.HasCheckConstraint(
                "ck_notification_jobs_template",
                "length(btrim(template_key)) BETWEEN 1 AND 128 AND jsonb_typeof(template_arguments) = 'object'");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.AllowedChannels).HasColumnType("text[]");
        builder.Property(job => job.TemplateArguments).HasColumnType("jsonb");
        builder.HasOne<BillingAccount>()
            .WithMany()
            .HasForeignKey(job => job.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => new
            {
                job.AccountId,
                job.ScopeKind,
                job.ScopeId,
                job.RecipientKey,
                job.Purpose,
                job.SourceKind,
                job.SourceId,
                job.SourceRevision,
                job.Threshold,
            })
            .IsUnique()
            .HasDatabaseName("ux_notification_jobs_semantic_key");

        builder.HasIndex(job => new { job.AccountId, job.DueAt, job.Id })
            .HasDatabaseName("ix_notification_jobs_account_due");

        builder.HasIndex(job => new { job.ScopeId, job.RecipientUserId, job.DueAt, job.Id })
            .HasFilter("scope_kind = 'Shop' AND recipient_user_id IS NOT NULL")
            .HasDatabaseName("ix_notification_jobs_shop_actor_due");

        builder.HasIndex(job => new { job.AccountId, job.RecipientUserId, job.DueAt, job.Id })
            .HasFilter("scope_kind = 'Account' AND recipient_user_id IS NOT NULL")
            .HasDatabaseName("ix_notification_jobs_account_actor_due");
    }
}

internal sealed class BillingNoticeContactConfiguration : IEntityTypeConfiguration<BillingNoticeContact>
{
    public void Configure(EntityTypeBuilder<BillingNoticeContact> builder)
    {
        builder.ToTable("billing_notice_contacts", table =>
        {
            table.HasCheckConstraint(
                "ck_billing_notice_contacts_ids",
                """
                id <> '00000000-0000-0000-0000-000000000000'
                AND account_id <> '00000000-0000-0000-0000-000000000000'
                AND approved_by_actor_id <> '00000000-0000-0000-0000-000000000000'
                AND operation_key <> '00000000-0000-0000-0000-000000000000'
                AND (merchant_user_id IS NULL OR merchant_user_id <> '00000000-0000-0000-0000-000000000000')
                AND (revoked_by_actor_id IS NULL OR revoked_by_actor_id <> '00000000-0000-0000-0000-000000000000')
                """);
            table.HasCheckConstraint(
                "ck_billing_notice_contacts_hashes",
                """
                octet_length(destination_hash) = 32
                AND (verification_token_hash IS NULL OR octet_length(verification_token_hash) = 32)
                AND length(btrim(protected_destination)) > 0
                AND length(btrim(destination_hint)) BETWEEN 3 AND 128
                """);
            table.HasCheckConstraint(
                "ck_billing_notice_contacts_locale",
                "locale IN ('en-IN', 'hi-IN', 'ta-IN', 'te-IN', 'bn-IN', 'ml-IN', 'kn-IN', 'mr-IN', 'gu-IN')");
            table.HasCheckConstraint(
                "ck_billing_notice_contacts_consent",
                "revision >= 1 AND (opted_in_at IS NULL OR verified_at IS NOT NULL)");
        });

        builder.HasKey(contact => contact.Id);
        builder.Property(contact => contact.Id).ValueGeneratedNever();
        builder.HasOne<BillingAccount>()
            .WithMany()
            .HasForeignKey(contact => contact.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(contact => new { contact.AccountId, contact.OperationKey })
            .IsUnique()
            .HasDatabaseName("ux_billing_notice_contacts_operation");

        builder.HasIndex(contact => new { contact.AccountId, contact.DestinationHash })
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ux_billing_notice_contacts_active_destination");

        builder.HasIndex(contact => new { contact.AccountId, contact.MerchantUserId })
            .IsUnique()
            .HasFilter("merchant_user_id IS NOT NULL AND revoked_at IS NULL")
            .HasDatabaseName("ux_billing_notice_contacts_active_user");

        builder.HasIndex(contact => new { contact.AccountId, contact.Id })
            .HasDatabaseName("ix_billing_notice_contacts_account");
    }
}

internal sealed class BillingNoticeContactEventConfiguration : IEntityTypeConfiguration<BillingNoticeContactEvent>
{
    public void Configure(EntityTypeBuilder<BillingNoticeContactEvent> builder)
    {
        builder.ToTable("billing_notice_contact_events", table =>
        {
            table.HasCheckConstraint(
                "ck_billing_notice_contact_events_ids",
                """
                id <> '00000000-0000-0000-0000-000000000000'
                AND contact_id <> '00000000-0000-0000-0000-000000000000'
                AND account_id <> '00000000-0000-0000-0000-000000000000'
                AND (actor_id IS NULL OR actor_id <> '00000000-0000-0000-0000-000000000000')
                """);
            table.HasCheckConstraint(
                "ck_billing_notice_contact_events_kind",
                "event_kind IN ('Approved', 'Verified', 'OptedIn', 'Revoked') AND revision >= 1");
        });

        builder.HasKey(evt => evt.Id);
        builder.Property(evt => evt.Id).ValueGeneratedNever();
        builder.HasOne<BillingNoticeContact>()
            .WithMany()
            .HasForeignKey(evt => evt.ContactId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillingAccount>()
            .WithMany()
            .HasForeignKey(evt => evt.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(evt => new { evt.ContactId, evt.Revision, evt.EventKind })
            .IsUnique()
            .HasDatabaseName("ux_billing_notice_contact_events_revision");
    }
}
