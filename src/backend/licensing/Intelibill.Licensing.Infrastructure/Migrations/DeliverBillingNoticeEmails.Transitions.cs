using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class DeliverBillingNoticeEmails
{
    private static void AddClaim(MigrationBuilder migration) =>
        migration.Sql("""
            CREATE INDEX ix_email_pending_due ON public.notification_email_deliveries(due_at, id) WHERE state = 'Pending';
            CREATE FUNCTION public.next_notice_email(p_due timestamptz, p_clock timestamptz)
                RETURNS TABLE(id uuid, account_id uuid, token uuid)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path TO pg_catalog AS $$
            BEGIN
                IF NOT coalesce(public.notification_planner_context(NULL) AND current_setting('app.notice_email_worker',true) = 'on',false)
                THEN RETURN; END IF;
                RETURN QUERY WITH expired AS MATERIALIZED (
                    SELECT d.id, d.state, d.attempts, d.lease_expires_at AS ready_at FROM public.notification_email_deliveries d
                     WHERE d.state IN ('Sending','Leased') AND d.lease_expires_at <= p_clock
                     ORDER BY d.lease_expires_at, d.id LIMIT 1 FOR UPDATE SKIP LOCKED
                ), retry AS MATERIALIZED (
                    SELECT d.id, d.state, d.attempts, d.next_attempt_at FROM public.notification_email_deliveries d
                     WHERE d.state = 'RetryPending' AND d.next_attempt_at <= p_clock AND d.due_at <= p_due
                     ORDER BY d.next_attempt_at, d.id LIMIT 1 FOR UPDATE SKIP LOCKED
                ), pending AS MATERIALIZED (
                    SELECT d.id, d.state, d.attempts, d.due_at FROM public.notification_email_deliveries d
                     WHERE d.state = 'Pending' AND d.due_at <= p_due
                     ORDER BY d.due_at, d.id LIMIT 1 FOR UPDATE SKIP LOCKED
                ), candidates AS (
                    SELECT * FROM expired UNION ALL SELECT * FROM retry UNION ALL SELECT * FROM pending
                ), due AS (
                    SELECT c.id, CASE WHEN c.state = 'Sending' THEN 'Uncertain'
                        WHEN c.attempts = 5 THEN 'Failed' ELSE 'Leased' END AS next_state
                    FROM candidates c ORDER BY c.ready_at, c.id LIMIT 1
                )
                UPDATE public.notification_email_deliveries d SET state = due.next_state,
                    attempts = d.attempts + CASE WHEN due.next_state = 'Leased' THEN 1 ELSE 0 END,
                    last_attempt_at = CASE WHEN due.next_state = 'Leased' THEN p_clock ELSE d.last_attempt_at END,
                    lease_token = CASE WHEN due.next_state = 'Leased' THEN gen_random_uuid() END,
                    lease_expires_at = CASE WHEN due.next_state = 'Leased' THEN p_clock + interval '60 seconds' END,
                    outcome_at = CASE WHEN due.next_state <> 'Leased' THEN p_clock END, next_attempt_at = NULL
                FROM due WHERE due.id = d.id RETURNING d.id, d.account_id, d.lease_token;
            END $$;
            REVOKE ALL ON FUNCTION public.next_notice_email(timestamptz,timestamptz) FROM PUBLIC;

            """);

    private static void AddAdvance(MigrationBuilder migration) =>
        migration.Sql("""
            CREATE FUNCTION public.advance_notice_email(
                p_account uuid, p_id uuid, p_token uuid, p_from text, p_to text, p_clock timestamptz)
                RETURNS boolean LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path TO pg_catalog AS $$
                WITH changed AS (
                    UPDATE public.notification_email_deliveries d SET
                        state = CASE WHEN p_to = 'RetryPending' AND d.attempts = 5 THEN 'Failed' ELSE p_to END,
                        lease_token = CASE WHEN p_to = 'Sending' THEN p_token END,
                        lease_expires_at = CASE WHEN p_to = 'Sending' THEN p_clock + interval '60 seconds' END,
                        next_attempt_at = CASE WHEN p_to = 'RetryPending' AND d.attempts < 5
                            THEN p_clock + interval '30 seconds' * (1 << (d.attempts - 1)) END,
                        outcome_at = CASE WHEN p_to <> 'Sending' AND (p_to <> 'RetryPending' OR d.attempts = 5) THEN p_clock END
                    WHERE public.notification_planner_context(p_account) AND current_setting('app.notice_email_worker',true) = 'on'
                        AND d.id = p_id AND d.account_id = p_account AND d.lease_token = p_token
                        AND d.state = p_from AND d.lease_expires_at > p_clock
                        AND ((p_from = 'Leased' AND p_to IN ('Sending','Suppressed','Failed','RetryPending'))
                            OR (p_from = 'Sending' AND p_to IN ('SmtpAccepted','Uncertain','Failed','RetryPending')))
                    RETURNING d.id
                )
                SELECT EXISTS (SELECT FROM changed)
            $$;
            REVOKE ALL ON FUNCTION public.advance_notice_email(uuid,uuid,uuid,text,text,timestamptz) FROM PUBLIC;
            """);
}
