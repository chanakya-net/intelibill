using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>SQL-owned immutable commercial evidence, like coverage terms and cancellation receipts.</summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261116230000_CheckoutPayments")]
public sealed partial class CheckoutPayments : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var (table, action) in new[]
        {
            ("checkout_orders", "billing.checkout.create"),
            ("checkout_order_shops", "billing.checkout.create"),
            ("checkout_order_acceptances", "billing.checkout.accept"),
        })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY checkout_read ON public.{table} FOR SELECT USING (
                    account_id = {Account} AND {LiveMember(table)});
                CREATE POLICY checkout_insert ON public.{table} FOR INSERT WITH CHECK (
                    account_id = {Account} AND {LiveMember(table)} AND EXISTS (
                        SELECT FROM public.merchant_billing_delegation_uses u
                        WHERE u.replay_id = {table}.replay_id AND u.account_id = {table}.account_id
                            AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid AND u.action = '{action}'));
                CREATE TRIGGER checkout_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }

        migrationBuilder.Sql($"""
            CREATE POLICY checkout_catalogue_read ON public.published_catalogue_offers FOR SELECT USING (
                EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = nullif(current_setting('app.catalogue_public_read', true), '')::uuid
                        AND u.account_id = {Account} AND u.actor_id = {Actor}
                        AND u.xmin = pg_current_xact_id()::xid AND u.action = 'billing.checkout.create' AND {LiveMember("u")}));
            """);
        migrationBuilder.Sql(Guards);
    }

    private static string LiveMember(string qualifier) => $"""
        EXISTS (SELECT FROM public.billing_account_members m
            WHERE m.account_id = {qualifier}.account_id AND m.merchant_user_id = {Actor}
              AND m.status = 1 AND m.role IN (1, 2))
        """;

    private const string Tables = """
        CREATE TABLE public.checkout_orders (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            order_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            term_id uuid,
            quote_revision int NOT NULL CHECK (quote_revision = 1),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            shop_count int NOT NULL CHECK (shop_count BETWEEN 1 AND 100000),
            shop_hash text NOT NULL CHECK (shop_hash ~ '^[a-f0-9]{64}$'),
            snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[a-f0-9]{64}$'),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            expires_at timestamptz NOT NULL CHECK (isfinite(expires_at) AND expires_at = created_at + interval '15 minutes'),
            snapshot jsonb NOT NULL CHECK (jsonb_typeof(snapshot) = 'object'),
            PRIMARY KEY (account_id, order_id), UNIQUE (account_id, operation_key), UNIQUE (replay_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );

        CREATE TABLE public.checkout_order_shops (
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            shop_name text NOT NULL CHECK (length(shop_name) BETWEEN 1 AND 250),
            affiliation_request_id uuid NOT NULL,
            replay_id uuid NOT NULL,
            PRIMARY KEY (account_id, order_id, shop_id),
            FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id)
                DEFERRABLE INITIALLY DEFERRED
        );

        CREATE TABLE public.checkout_order_acceptances (
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            membership_revision int NOT NULL CHECK (membership_revision > 0),
            request_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            quote_revision int NOT NULL CHECK (quote_revision = 1),
            snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[a-f0-9]{64}$'),
            customer_terms_version text NOT NULL CHECK (length(customer_terms_version) BETWEEN 1 AND 80),
            refund_policy_version text NOT NULL CHECK (length(refund_policy_version) BETWEEN 1 AND 80),
            accepted_at timestamptz NOT NULL CHECK (isfinite(accepted_at)),
            PRIMARY KEY (account_id, order_id), UNIQUE (account_id, operation_key), UNIQUE (replay_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id)
        );
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.checkout_orders, public.checkout_order_shops, public.checkout_order_acceptances IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.checkout_orders NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.checkout_orders) THEN
                RAISE EXCEPTION 'Checkout evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP POLICY checkout_catalogue_read ON public.published_catalogue_offers;
        DROP TABLE public.checkout_order_acceptances, public.checkout_order_shops, public.checkout_orders;
        DROP FUNCTION public.guard_checkout_order(), public.guard_checkout_shop(), public.require_checkout_shop_seal(),
            public.guard_checkout_acceptance();
        """);
}
