using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class PlanLicenseNotices
{
    private static void RetainNoticeEvidence(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION public.refuse_notification_evidence_mutation() RETURNS trigger
                LANGUAGE plpgsql
                SET search_path TO 'pg_catalog'
            AS $$
            BEGIN
                RAISE EXCEPTION 'Notification job and consent history are append-only' USING ERRCODE = '55000';
            END;
            $$;

            CREATE TRIGGER notification_jobs_immutable
                BEFORE UPDATE OR DELETE OR TRUNCATE ON public.notification_jobs
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();

            CREATE TRIGGER billing_notice_contact_events_immutable
                BEFORE UPDATE OR DELETE OR TRUNCATE ON public.billing_notice_contact_events
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();

            CREATE TRIGGER billing_notice_contact_operations_immutable
                BEFORE UPDATE OR DELETE OR TRUNCATE ON public.billing_notice_contact_operations
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();

            CREATE TRIGGER billing_notice_contacts_retain
                BEFORE DELETE OR TRUNCATE ON public.billing_notice_contacts
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();
            """);

    private static void EnableNoticeIsolation(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE public.notification_jobs ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.notification_jobs FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_notice_contacts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_notice_contacts FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_notice_contact_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_notice_contact_events FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_notice_contact_operations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_notice_contact_operations FORCE ROW LEVEL SECURITY;

            DO $$
            DECLARE
                discovery_table text;
            BEGIN
                EXECUTE format(
                    'CREATE POLICY notification_jobs_owner ON public.notification_jobs TO %I USING (true) WITH CHECK (true)',
                    current_user);
                EXECUTE format(
                    'CREATE POLICY billing_notice_contacts_owner ON public.billing_notice_contacts TO %I USING (true) WITH CHECK (true)',
                    current_user);
                EXECUTE format(
                    'CREATE POLICY billing_notice_contact_events_owner ON public.billing_notice_contact_events TO %I USING (true) WITH CHECK (true)',
                    current_user);
                EXECUTE format(
                    'CREATE POLICY billing_notice_contact_operations_owner ON public.billing_notice_contact_operations TO %I USING (true) WITH CHECK (true)',
                    current_user);
                FOREACH discovery_table IN ARRAY ARRAY[
                    'subscription_coverage_terms',
                    'subscription_term_facts',
                    'trial_history',
                    'trial_extensions',
                    'payment_trial_conversions',
                    'complimentary_grants',
                    'operational_closures',
                    'payment_fulfilments',
                    'paid_fresh_starts'
                ] LOOP
                    EXECUTE format(
                        'CREATE POLICY notification_planner_discovery ON public.%I FOR SELECT TO %I USING (true)',
                        discovery_table,
                        current_user);
                END LOOP;
            END;
            $$;
            """);

    private static void AddNoticePolicies(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE POLICY notification_jobs_account_read ON public.notification_jobs
                FOR SELECT
                USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

            CREATE POLICY notification_jobs_shop_actor_read ON public.notification_jobs
                FOR SELECT
                USING (
                    scope_kind = 'Shop'
                    AND scope_id = NULLIF(current_setting('app.status_shop_id', true), '')::uuid
                    AND recipient_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid);

            CREATE POLICY notification_jobs_account_insert ON public.notification_jobs
                FOR INSERT
                WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

            CREATE POLICY billing_notice_contacts_account_scope ON public.billing_notice_contacts
                FOR ALL
                USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid)
                WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

            CREATE POLICY billing_notice_contacts_holder_read ON public.billing_notice_contacts
                FOR SELECT
                USING (merchant_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid);

            CREATE POLICY billing_notice_contacts_holder_update ON public.billing_notice_contacts
                FOR UPDATE
                USING (merchant_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid)
                WITH CHECK (merchant_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid);

            CREATE POLICY billing_notice_contact_events_account_read ON public.billing_notice_contact_events
                FOR SELECT
                USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

            CREATE POLICY billing_notice_contact_events_account_insert ON public.billing_notice_contact_events
                FOR INSERT
                WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

            CREATE POLICY billing_notice_contact_operations_account_read ON public.billing_notice_contact_operations
                FOR SELECT
                USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

            CREATE POLICY billing_notice_contact_operations_account_insert ON public.billing_notice_contact_operations
                FOR INSERT
                WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);
            """);
}
