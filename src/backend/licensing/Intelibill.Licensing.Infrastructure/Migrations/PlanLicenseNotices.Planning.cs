using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class PlanLicenseNotices
{
    private static void AddPlannerContext(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION public.notification_planner_context(p_account uuid) RETURNS boolean
                LANGUAGE sql STABLE SET search_path TO pg_catalog AS $$
                SELECT current_setting('app.notification_planner', true) = 'on'
                    AND nullif(current_setting('app.billing_account_id', true), '') IS NOT DISTINCT FROM p_account::text
                    AND NOT EXISTS (SELECT FROM unnest(ARRAY[
                        'app.active_shop_id', 'app.affiliation_request_id',
                        'app.affiliation_shop_id', 'app.billing_actor_id',
                        'app.catalogue_admin_id', 'app.catalogue_public_read',
                        'app.coverage_exception_applying_id', 'app.coverage_exception_id',
                        'app.coverage_transfer_id', 'app.current_user_id',
                        'app.development_gateway', 'app.entitlement_account_id',
                        'app.entitlement_replay_id', 'app.exception_admin_id',
                        'app.exception_evaluated_at', 'app.exception_session_id',
                        'app.gateway_account', 'app.gateway_configuration',
                        'app.gateway_environment', 'app.grant_admin_id',
                        'app.manual_admin_id', 'app.manual_order_id',
                        'app.manual_receipt_id', 'app.manual_session_id',
                        'app.payment_attempt_id', 'app.provider_admin_id',
                        'app.provider_session_id', 'app.reduction_due_scan',
                        'app.replacement_evaluated_at', 'app.replacement_id',
                        'app.restriction_account_id', 'app.slot_assignment_id',
                        'app.status_shop_id', 'app.trial_delivery_id',
                        'app.trial_extension_admin_id', 'app.trial_preview_replay'
                    ]) AS scope(name) WHERE nullif(current_setting(scope.name, true), '') IS NOT NULL)
            $$;
            REVOKE ALL ON FUNCTION public.notification_planner_context(uuid) FROM PUBLIC;

            """);

    private static void AddPlannerDiscovery(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION public.due_notification_planning(
                p_now timestamp with time zone,
                p_after_account uuid,
                p_limit integer)
                RETURNS TABLE(account_id uuid)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path TO 'pg_catalog'
            AS $$
                SELECT DISTINCT t.account_id
                FROM public.subscription_coverage_terms t
                LEFT JOIN public.subscription_term_facts f
                    ON f.account_id = t.account_id AND f.term_id = t.term_id
                LEFT JOIN public.trial_history h
                    ON h.account_id = t.account_id AND h.term_id = t.term_id
                LEFT JOIN LATERAL (
                    SELECT e.ends_at
                    FROM public.trial_extensions e
                    WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id
                    ORDER BY e.revision DESC
                    LIMIT 1
                ) ext ON true
                LEFT JOIN public.payment_trial_conversions x
                    ON x.account_id = h.account_id AND x.shop_id = h.shop_id
                LEFT JOIN public.complimentary_grants g
                    ON g.account_id = t.account_id AND g.term_id = t.term_id
                WHERE (p_after_account IS NULL OR t.account_id > p_after_account)
                  AND g.grant_id IS NULL
                  AND NOT EXISTS (
                      SELECT FROM public.operational_closures oc
                      WHERE oc.account_id = t.account_id AND oc.shop_id IS NULL
                  )
                  AND (
                      (f.basis = 'Paid'
                          AND t.ends_at + interval '7 days' >= p_now
                          AND NOT EXISTS (
                              SELECT FROM public.payment_fulfilments pf
                              WHERE pf.account_id = t.account_id
                                AND pf.predecessor_term_id = t.term_id
                                AND pf.fulfilled_at <= p_now
                                AND NOT EXISTS (
                                    SELECT FROM public.paid_fresh_starts pfs
                                    WHERE pfs.account_id = pf.account_id
                                      AND pfs.order_id = pf.order_id
                                      AND pfs.kind = 'RestoredAccess'
                                )
                          ))
                      OR (h.term_id IS NOT NULL
                          AND COALESCE(ext.ends_at, t.ends_at) > p_now
                          AND (x.paid_starts_at IS NULL OR x.paid_starts_at > p_now))
                  )
                  AND public.notification_planner_context(NULL)
                ORDER BY t.account_id
                LIMIT LEAST(GREATEST(COALESCE(p_limit, 20), 1), 100)
            $$;

            REVOKE ALL ON FUNCTION public.due_notification_planning(timestamp with time zone, uuid, integer) FROM PUBLIC;

            """);

    private static void AddPlannerMembers(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION public.account_notification_billing_members(
                p_account uuid,
                p_after_user uuid,
                p_limit integer)
                RETURNS TABLE(id uuid, merchant_user_id uuid, role integer, revision integer)
                LANGUAGE sql STABLE SECURITY DEFINER
                SET search_path TO 'pg_catalog'
            AS $$
                SELECT m.id, m.merchant_user_id, m.role, m.revision
                FROM public.billing_account_members m
                WHERE m.account_id = p_account
                  AND m.status = 1
                  AND m.role IN (1, 2)
                  AND (p_after_user IS NULL OR m.merchant_user_id > p_after_user)
                  AND public.notification_planner_context(p_account)
                ORDER BY m.merchant_user_id
                LIMIT LEAST(GREATEST(COALESCE(p_limit, 50), 1), 100)
            $$;

            REVOKE ALL ON FUNCTION public.account_notification_billing_members(uuid, uuid, integer) FROM PUBLIC;
            """);

    private static void AddChannelEvidence(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE public.notification_jobs ADD CONSTRAINT uq_notification_job_account UNIQUE(account_id, id);
            CREATE TABLE public.notification_channel_additions (
                job_id uuid PRIMARY KEY,
                account_id uuid NOT NULL,
                channel text NOT NULL CHECK(channel IN ('Email', 'InApp')),
                recipient_user_id uuid NOT NULL,
                authority_revision integer NOT NULL CHECK(authority_revision > 0),
                destination_hash bytea, protected_destination text, destination_hint text,
                recorded_at timestamptz NOT NULL,
                FOREIGN KEY (account_id, job_id) REFERENCES public.notification_jobs(account_id, id),
                CHECK ((channel = 'Email' AND destination_hash IS NOT NULL AND octet_length(destination_hash) = 32
                        AND protected_destination IS NOT NULL AND destination_hint IS NOT NULL)
                    OR (channel = 'InApp' AND destination_hash IS NULL AND protected_destination IS NULL AND destination_hint IS NULL))
            );
            CREATE TRIGGER notification_channel_additions_immutable
                BEFORE UPDATE OR DELETE OR TRUNCATE ON public.notification_channel_additions
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();
            ALTER TABLE public.notification_channel_additions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.notification_channel_additions FORCE ROW LEVEL SECURITY;
            CREATE POLICY channel_additions_account_read ON public.notification_channel_additions FOR SELECT
                USING (account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
            CREATE POLICY channel_additions_account_insert ON public.notification_channel_additions FOR INSERT
                WITH CHECK (account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
            DO $$ BEGIN
                EXECUTE format('CREATE POLICY channel_additions_owner ON public.notification_channel_additions TO %I USING (true)',
                    current_user);
            END $$;

            CREATE VIEW public.effective_notification_jobs WITH (security_invoker = true) AS
            SELECT j.id, j.account_id, j.scope_kind, j.scope_id, j.recipient_key,
                coalesce(j.recipient_user_id, a.recipient_user_id) AS recipient_user_id, j.contact_id,
                CASE WHEN a.channel = 'InApp' AND j.scope_kind = 'Account' THEN 'CommercialBillingRole' ELSE j.audience END AS audience,
                j.purpose, j.source_kind, j.source_id, j.source_revision, j.threshold, j.due_at, j.effective_deadline_at,
                j.time_zone, j.locale, CASE WHEN a.job_id IS NULL THEN j.allowed_channels ELSE ARRAY['InApp', 'Email'] END AS allowed_channels,
                j.status, j.template_key, j.template_arguments, coalesce(j.destination_hint, a.destination_hint) AS destination_hint,
                coalesce(j.destination_hash, a.destination_hash) AS destination_hash,
                coalesce(j.protected_destination, a.protected_destination) AS protected_destination, j.planned_at, j.created_at
            FROM public.notification_jobs j
            LEFT JOIN public.notification_channel_additions a ON a.job_id = j.id AND a.account_id = j.account_id;
            """);
}
