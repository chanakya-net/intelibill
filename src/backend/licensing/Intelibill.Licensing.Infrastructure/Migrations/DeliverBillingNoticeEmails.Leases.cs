using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class DeliverBillingNoticeEmails
{
    private static void SecureDeliveries(MigrationBuilder migration) =>
        migration.Sql("""
            CREATE TABLE public.notification_email_events (
                id uuid PRIMARY KEY,
                delivery_id uuid NOT NULL REFERENCES public.notification_email_deliveries(id),
                account_id uuid NOT NULL,
                attempt integer NOT NULL CHECK (attempt BETWEEN 1 AND 5),
                state text NOT NULL CHECK (state IN ('Leased','Sending','RetryPending','SmtpAccepted','Uncertain','Suppressed','Failed')),
                reason text NOT NULL,
                recorded_at timestamptz NOT NULL
            );
            CREATE INDEX notification_email_events_delivery ON public.notification_email_events(delivery_id, attempt);
            ALTER TABLE public.notification_email_deliveries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.notification_email_deliveries FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.notification_email_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.notification_email_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY read_email_delivery ON public.notification_email_deliveries FOR SELECT
                USING (account_id::text = current_setting('app.billing_account_id', true));
            CREATE POLICY enqueue_email_delivery ON public.notification_email_deliveries FOR INSERT
                WITH CHECK (account_id::text = current_setting('app.billing_account_id', true) AND state = 'Pending' AND attempts = 0);
            CREATE POLICY read_email_event ON public.notification_email_events FOR SELECT
                USING (account_id::text = current_setting('app.billing_account_id', true));
            DO $$ BEGIN
                EXECUTE format('CREATE POLICY email_discovery ON public.notification_email_deliveries TO %I USING (true)', current_user);
                EXECUTE format('CREATE POLICY email_event_owner ON public.notification_email_events TO %I USING (true)', current_user);
            END $$;
            CREATE FUNCTION public.record_notice_email_event() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER SET search_path TO pg_catalog AS $$
            BEGIN
                INSERT INTO public.notification_email_events(id, delivery_id, account_id, attempt, state, reason, recorded_at)
                VALUES (gen_random_uuid(), NEW.id, NEW.account_id, NEW.attempts, NEW.state,
                    CASE
                        WHEN NEW.state = 'Uncertain' AND OLD.lease_expires_at <=
                            nullif(current_setting('app.notice_email_clock',true),'')::timestamptz THEN 'SendLeaseExpired'
                        WHEN NEW.state = 'Uncertain' THEN 'AcknowledgementUnknown'
                        WHEN NEW.state = 'SmtpAccepted' THEN 'SmtpAcknowledged'
                        WHEN NEW.state = 'Suppressed' THEN 'AuthorityUnavailableForRecipient'
                        WHEN NEW.state IN ('Failed','RetryPending') AND OLD.state = 'Sending' THEN 'SmtpRejected'
                        WHEN NEW.state = 'Failed' AND NEW.attempts = 5 THEN 'AttemptLimitReached'
                        WHEN NEW.state = 'RetryPending' THEN 'BeforeSubmission'
                        ELSE NEW.state END,
                    coalesce(NEW.outcome_at, nullif(current_setting('app.notice_email_clock',true),'')::timestamptz, NEW.last_attempt_at));
                RETURN NEW;
            END $$;
            REVOKE ALL ON FUNCTION public.record_notice_email_event() FROM PUBLIC;
            CREATE TRIGGER notice_email_history AFTER UPDATE ON public.notification_email_deliveries
                FOR EACH ROW EXECUTE FUNCTION public.record_notice_email_event();
            CREATE TRIGGER notice_email_history_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON public.notification_email_events
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();
            CREATE TRIGGER notice_email_retain BEFORE DELETE OR TRUNCATE ON public.notification_email_deliveries
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();
            """);

    private static void RemoveEmptyDeliveryLedger(MigrationBuilder migration) =>
        migration.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.notification_email_deliveries) THEN
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Email delivery history must be retained';
                END IF;
            END $$;
            DROP FUNCTION public.advance_notice_email(uuid,uuid,uuid,text,text,timestamptz);
            DROP FUNCTION public.next_notice_email(timestamptz,timestamptz);
            DROP TRIGGER notice_email_history ON public.notification_email_deliveries;
            DROP FUNCTION public.record_notice_email_event();
            DROP TABLE public.notification_email_events;
            """);
}
