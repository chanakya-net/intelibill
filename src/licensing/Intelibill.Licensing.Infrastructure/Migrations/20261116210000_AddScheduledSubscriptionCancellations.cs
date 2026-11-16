using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261116210000_AddScheduledSubscriptionCancellations")]
public sealed class AddScheduledSubscriptionCancellations : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Shop = "nullif(current_setting('app.status_shop_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "scheduled_subscription_changes", "scheduled_subscription_change_shops", "subscription_cancellation_receipts" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(Guards);
    }

    private const string Tables = """
        CREATE TABLE public.scheduled_subscription_changes (
            change_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            term_id uuid NOT NULL,
            revision bigint NOT NULL CHECK (revision = 1),
            kind text NOT NULL CHECK (kind = 'ScheduleCancellation'),
            effective_at timestamptz NOT NULL CHECK (isfinite(effective_at) AND effective_at > recorded_at),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            policy_version text NOT NULL CHECK (policy_version = 'term-boundary/1'),
            shop_count integer NOT NULL CHECK (shop_count > 0),
            shop_hash text NOT NULL CHECK (shop_hash ~ '^[0-9a-f]{64}$'),
            UNIQUE (account_id, term_id, revision),
            UNIQUE (account_id, change_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );
        CREATE TABLE public.scheduled_subscription_change_shops (
            account_id uuid NOT NULL,
            change_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            request_id uuid NOT NULL,
            shop_name text NOT NULL CHECK (length(shop_name) BETWEEN 1 AND 250),
            PRIMARY KEY (change_id, shop_id),
            FOREIGN KEY (account_id, change_id) REFERENCES public.scheduled_subscription_changes(account_id, change_id),
            FOREIGN KEY (account_id, shop_id, request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id)
        );
        CREATE INDEX scheduled_subscription_changes_by_shop ON public.scheduled_subscription_change_shops (shop_id, change_id);
        CREATE TABLE public.subscription_cancellation_receipts (
            change_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            operation_key uuid NOT NULL CHECK (operation_key <> '00000000-0000-0000-0000-000000000000'),
            actor_id uuid NOT NULL,
            membership_revision integer NOT NULL CHECK (membership_revision > 0),
            request_id uuid NOT NULL,
            payload_hash text NOT NULL CHECK (payload_hash ~ '^[0-9a-f]{64}$'),
            source_hash text NOT NULL CHECK (source_hash ~ '^[0-9a-f]{64}$'),
            UNIQUE (account_id, operation_key),
            FOREIGN KEY (account_id, change_id) REFERENCES public.scheduled_subscription_changes(account_id, change_id)
        );
        """;

    private const string Policies = $"""
        CREATE POLICY cancellation_read ON public.scheduled_subscription_changes FOR SELECT USING (
            account_id = {Account} OR EXISTS (
                SELECT FROM public.scheduled_subscription_change_shops s
                WHERE s.change_id = scheduled_subscription_changes.change_id AND s.shop_id = {Shop}));
        CREATE POLICY cancellation_shop_read ON public.scheduled_subscription_change_shops FOR SELECT
            USING (account_id = {Account} OR shop_id = {Shop});
        CREATE POLICY cancellation_receipt_read ON public.subscription_cancellation_receipts FOR SELECT
            USING (account_id = {Account} AND {Actor} IS NOT NULL);
        CREATE POLICY cancellation_insert ON public.scheduled_subscription_changes FOR INSERT
            WITH CHECK (account_id = {Account} AND {Actor} IS NOT NULL);
        CREATE POLICY cancellation_shop_insert ON public.scheduled_subscription_change_shops FOR INSERT
            WITH CHECK (account_id = {Account} AND {Actor} IS NOT NULL);
        CREATE POLICY cancellation_receipt_insert ON public.subscription_cancellation_receipts FOR INSERT
            WITH CHECK (account_id = {Account} AND actor_id = {Actor});
        """;

    internal const string Guards = """
        CREATE FUNCTION public.guard_subscription_cancellation() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.subscription_coverage_terms t JOIN public.subscription_term_facts f USING (account_id, term_id)
                WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND f.basis = 'Paid'
                    AND f.price_paise IS NOT NULL AND t.starts_at <= NEW.recorded_at
                    AND t.ends_at = NEW.effective_at AND t.capacity >= NEW.shop_count
            ) OR NOT EXISTS (
                SELECT FROM public.subscription_cancellation_receipts r
                JOIN public.billing_account_members m ON m.account_id = r.account_id AND m.merchant_user_id = r.actor_id
                WHERE r.change_id = NEW.change_id AND m.status = 1 AND m.role IN (1, 2)
                    AND m.revision = r.membership_revision
            ) OR NEW.shop_count <> (SELECT count(*) FROM public.scheduled_subscription_change_shops s WHERE s.change_id = NEW.change_id)
            OR NEW.shop_count <> (SELECT count(*) FROM public.shop_affiliations a
                WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Cancellation requires the paid boundary, complete shop scope and an acceptance receipt'
                    USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER require_cancellation_evidence AFTER INSERT ON public.scheduled_subscription_changes
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.guard_subscription_cancellation();

        CREATE FUNCTION public.guard_subscription_cancellation_shop() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.scheduled_subscription_changes c
                JOIN public.shop_affiliations a ON a.account_id = c.account_id AND a.term_id = c.term_id
                WHERE c.change_id = NEW.change_id AND a.shop_id = NEW.shop_id AND a.request_id = NEW.request_id
                    AND a.shop_name = NEW.shop_name AND a.applied_at <= c.recorded_at AND a.coverage_ends_at = c.effective_at
            ) OR EXISTS (SELECT FROM public.subscription_cancellation_receipts r WHERE r.change_id = NEW.change_id) THEN
                RAISE EXCEPTION 'Cancellation shop must belong to the active scope before acceptance' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER require_cancellation_shop BEFORE INSERT ON public.scheduled_subscription_change_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_subscription_cancellation_shop();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.scheduled_subscription_changes, public.scheduled_subscription_change_shops,
            public.subscription_cancellation_receipts IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.scheduled_subscription_changes NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.scheduled_subscription_changes) THEN
                RAISE EXCEPTION 'Subscription cancellation evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.subscription_cancellation_receipts, public.scheduled_subscription_change_shops,
            public.scheduled_subscription_changes;
        DROP FUNCTION public.guard_subscription_cancellation_shop();
        DROP FUNCTION public.guard_subscription_cancellation();
        """);
}
