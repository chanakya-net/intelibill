using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class RecordNoticeSuppressionReasons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => ReplaceEventFunction(migrationBuilder, """
        WHEN NEW.job_id IS NOT NULL AND NEW.state = 'Suppressed' AND current_setting('app.notice_email_reason',true)
            IN ('ScopeClosed','SourceNoLongerCurrent','WindowElapsed','RestrictionChanged','RecipientIneligible')
            THEN current_setting('app.notice_email_reason',true)
        WHEN NEW.job_id IS NOT NULL AND NEW.state = 'Suppressed' THEN 'Suppressed'
        WHEN OLD.state = 'Sending' AND NEW.state IN ('RetryPending','Failed')
            AND current_setting('app.notice_email_reason',true) = 'BeforeSubmission' THEN 'BeforeSubmission'
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => ReplaceEventFunction(migrationBuilder, "");

    private static void ReplaceEventFunction(MigrationBuilder migrationBuilder, string reasonCases) =>
        migrationBuilder.Sql($$"""
            CREATE OR REPLACE FUNCTION public.record_notice_email_event() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER SET search_path TO pg_catalog AS $$
            BEGIN
                INSERT INTO public.notification_email_events(id, delivery_id, account_id, attempt, state, reason, recorded_at)
                VALUES (gen_random_uuid(), NEW.id, NEW.account_id, NEW.attempts, NEW.state,
                    CASE
                        WHEN NEW.state = 'Uncertain' AND OLD.lease_expires_at <=
                            nullif(current_setting('app.notice_email_clock',true),'')::timestamptz THEN 'SendLeaseExpired'
                        WHEN NEW.state = 'Uncertain' THEN 'AcknowledgementUnknown'
                        WHEN NEW.state = 'SmtpAccepted' THEN 'SmtpAcknowledged'
                        {{reasonCases}}
                        WHEN NEW.state = 'Suppressed' THEN 'AuthorityUnavailableForRecipient'
                        WHEN NEW.state IN ('Failed','RetryPending') AND OLD.state = 'Sending' THEN 'SmtpRejected'
                        WHEN NEW.state = 'Failed' AND NEW.attempts = 5 THEN 'AttemptLimitReached'
                        WHEN NEW.state = 'RetryPending' THEN 'BeforeSubmission'
                        ELSE NEW.state END,
                    coalesce(NEW.outcome_at, nullif(current_setting('app.notice_email_clock',true),'')::timestamptz, NEW.last_attempt_at));
                RETURN NEW;
            END $$;
            """);
}
