using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-01: SQL-owned immutable capacity-change quote evidence (header plus its sealed named shops). A quote never
/// creates a checkout order, payment, term or coverage. Apply before the licensing API that writes quotes and before
/// any merchant API that relays them. Down refuses while any quote is retained.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261124110000_CapacityChangesRefundsAndDocuments")]
public sealed partial class CapacityChangesRefundsAndDocuments : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";
    private const string QuoteAction = "billing.capacity-change.quote";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "capacity_change_quotes", "capacity_change_quote_shops" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY capacity_quote_read ON public.{table} FOR SELECT USING (
                    account_id = {Account} AND {LiveOwner(table)});
                CREATE POLICY capacity_quote_insert ON public.{table} FOR INSERT WITH CHECK (
                    account_id = {Account} AND {LiveOwner(table)} AND EXISTS (
                        SELECT FROM public.merchant_billing_delegation_uses u
                        WHERE u.replay_id = {table}.replay_id AND u.account_id = {table}.account_id
                            AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid AND u.action = '{QuoteAction}'));
                CREATE TRIGGER capacity_quote_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }

        migrationBuilder.Sql($"""
            CREATE POLICY capacity_quote_catalogue_read ON public.published_catalogue_offers FOR SELECT USING (
                EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = nullif(current_setting('app.catalogue_public_read', true), '')::uuid
                        AND u.account_id = {Account} AND u.actor_id = {Actor}
                        AND u.xmin = pg_current_xact_id()::xid AND u.action = '{QuoteAction}' AND {LiveOwner("u")}));
            """);
        migrationBuilder.Sql(Guards);
    }

    private static string LiveOwner(string qualifier) => $"""
        EXISTS (SELECT FROM public.billing_account_members m
            WHERE m.account_id = {qualifier}.account_id AND m.merchant_user_id = {Actor} AND m.status = 1 AND m.role = 1)
        """;

    private const string Tables = """
        CREATE TABLE public.capacity_change_quotes (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            quote_id uuid NOT NULL,
            term_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            outcome text NOT NULL CHECK (outcome IN ('Payable', 'AtRenewalOrCourtesy', 'RenewalRequired', 'NewTermRequired',
                'TrialConversion', 'PaymentUnavailable')),
            reason text CHECK (length(reason) BETWEEN 1 AND 80),
            source_plan_version_id uuid,
            target_plan_version_id uuid NOT NULL,
            prior_capacity int NOT NULL CHECK (prior_capacity BETWEEN 1 AND 100000),
            target_capacity int NOT NULL CHECK (target_capacity BETWEEN prior_capacity AND 100000),
            prior_member_allowance int NOT NULL CHECK (prior_member_allowance BETWEEN 0 AND 100000),
            target_member_allowance int NOT NULL CHECK (target_member_allowance BETWEEN prior_member_allowance AND 100000),
            shop_count int NOT NULL CHECK (shop_count BETWEEN 1 AND prior_capacity),
            shop_hash text NOT NULL CHECK (shop_hash ~ '^[a-f0-9]{64}$'),
            source_hash text NOT NULL CHECK (source_hash ~ '^[a-f0-9]{64}$'),
            purchased_gross_paise bigint CHECK (purchased_gross_paise BETWEEN 0 AND 1000000000000),
            target_gross_paise bigint NOT NULL CHECK (target_gross_paise BETWEEN 1 AND 1000000000000),
            difference_paise bigint CHECK (difference_paise = target_gross_paise - purchased_gross_paise),
            amount_paise bigint CHECK (amount_paise BETWEEN 1 AND difference_paise),
            interval_starts_at timestamptz NOT NULL CHECK (isfinite(interval_starts_at)),
            paid_through timestamptz NOT NULL CHECK (isfinite(paid_through) AND paid_through > interval_starts_at),
            total_seconds bigint CHECK (total_seconds = extract(epoch FROM paid_through - interval_starts_at)),
            remaining_seconds bigint CHECK (remaining_seconds BETWEEN 0 AND total_seconds),
            proration_version text NOT NULL CHECK (proration_version = 'proration/1'),
            provider_id text CHECK (length(provider_id) BETWEEN 1 AND 80),
            provider_account_id text CHECK (length(provider_account_id) BETWEEN 1 AND 200),
            provider_environment text CHECK (length(provider_environment) BETWEEN 1 AND 40),
            provider_configuration_version text CHECK (length(provider_configuration_version) BETWEEN 1 AND 200),
            routing_revision bigint CHECK (routing_revision >= 0),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            expires_at timestamptz NOT NULL CHECK (expires_at = created_at + interval '15 minutes'),
            snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[a-f0-9]{64}$'),
            snapshot jsonb NOT NULL CHECK (jsonb_typeof(snapshot) = 'object'),
            PRIMARY KEY (account_id, quote_id), UNIQUE (account_id, operation_key), UNIQUE (replay_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            -- Only a payable quote carries an amount, a priced interval and the frozen provider pin.
            CHECK ((outcome = 'Payable') = (amount_paise IS NOT NULL)),
            CHECK (outcome <> 'Payable' OR (num_nonnulls(purchased_gross_paise, difference_paise, total_seconds,
                remaining_seconds, provider_id, provider_account_id, provider_environment, provider_configuration_version,
                routing_revision) = 9 AND created_at >= interval_starts_at AND created_at < paid_through)),
            CHECK (outcome = 'Payable' OR num_nonnulls(provider_id, provider_account_id, provider_environment,
                provider_configuration_version, routing_revision) = 0),
            CHECK ((difference_paise IS NULL) = (total_seconds IS NULL) AND (total_seconds IS NULL) = (remaining_seconds IS NULL))
        );

        CREATE TABLE public.capacity_change_quote_shops (
            account_id uuid NOT NULL,
            quote_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            shop_name text NOT NULL CHECK (length(shop_name) BETWEEN 1 AND 250),
            affiliation_request_id uuid NOT NULL,
            replay_id uuid NOT NULL,
            PRIMARY KEY (account_id, quote_id, shop_id),
            FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, quote_id) REFERENCES public.capacity_change_quotes(account_id, quote_id)
                DEFERRABLE INITIALLY DEFERRED
        );
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.capacity_change_quotes, public.capacity_change_quote_shops IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.capacity_change_quotes NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.capacity_change_quote_shops NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.capacity_change_quotes) OR EXISTS (SELECT FROM public.capacity_change_quote_shops) THEN
                RAISE EXCEPTION 'Retained capacity-change quotes prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP POLICY capacity_quote_catalogue_read ON public.published_catalogue_offers;
        DROP TABLE public.capacity_change_quote_shops, public.capacity_change_quotes;
        DROP FUNCTION public.guard_capacity_quote(), public.guard_capacity_quote_shop(), public.require_capacity_quote_seal();
        """);
}
