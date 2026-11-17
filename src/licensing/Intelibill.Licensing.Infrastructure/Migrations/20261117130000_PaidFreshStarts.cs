using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>Retains the trial and restriction evidence used to start or hold a new paid period.</summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261117130000_PaidFreshStarts")]
public sealed partial class PaidFreshStarts : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string Receipt = "nullif(current_setting('app.manual_receipt_id', true), '')::uuid";
    private const string Shop = "nullif(current_setting('app.status_shop_id', true), '')::uuid";
    private const string ManualOrder = "nullif(current_setting('app.manual_order_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        Retain(migrationBuilder, "checkout_trial_sources", $"account_id = {Account}",
            $"account_id = {Account} AND nullif(current_setting('app.billing_actor_id', true), '') IS NOT NULL");
        Retain(migrationBuilder, "payment_trial_conversions", $"account_id = {Account} OR shop_id = {Shop}", MoneyScope);
        Retain(migrationBuilder, "payment_restoration_holds", $"account_id = {Account}",
            $"account_id = {Account} AND (attempt_id = {Attempt} OR manual_receipt_id = {Receipt} AND order_id = {ManualOrder})");
        Retain(migrationBuilder, "paid_fresh_starts", $"account_id = {Account}", MoneyScope);
        migrationBuilder.Sql(SourceGuards);
        migrationBuilder.Sql(RestorationGuards);
        migrationBuilder.Sql(PaymentGuards);
        migrationBuilder.Sql(FulfilmentGuards);
        migrationBuilder.Sql(AdjustmentGuards);
        Retain(migrationBuilder, "service_restoration_adjustments", $"account_id = {Account}",
            $"account_id = {Account} AND {GrantAdmin} IS NOT NULL");
    }

    private static string MoneyScope =>
        $"account_id = {Account} AND ({Attempt} IS NOT NULL OR {Receipt} IS NOT NULL AND order_id = {ManualOrder})";

    private static void Retain(MigrationBuilder builder, string table, string read, string insert) => builder.Sql($"""
        ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
        CREATE POLICY scoped_read ON public.{table} FOR SELECT USING ({read});
        CREATE POLICY scoped_insert ON public.{table} FOR INSERT WITH CHECK ({insert});
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """);

    private const string Tables = """
        CREATE TABLE public.checkout_trial_sources (
            account_id uuid NOT NULL, order_id uuid NOT NULL, shop_id uuid NOT NULL, trial_term_id uuid NOT NULL,
            revision integer NOT NULL CHECK (revision >= 0),
            ends_at timestamptz NOT NULL CHECK (isfinite(ends_at)),
            PRIMARY KEY (account_id, order_id, shop_id),
            FOREIGN KEY (account_id, order_id, shop_id) REFERENCES public.checkout_order_shops(account_id, order_id, shop_id),
            FOREIGN KEY (shop_id) REFERENCES public.trial_history(shop_id)
        );
        CREATE TABLE public.paid_fresh_starts (
            account_id uuid NOT NULL, order_id uuid NOT NULL, term_id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('FirstPurchase', 'TrialConversion', 'RestoredAccess')),
            effective_at timestamptz NOT NULL CHECK (isfinite(effective_at)),
            ready_at timestamptz NOT NULL CHECK (isfinite(ready_at) AND ready_at >= effective_at),
            account_revision bigint NOT NULL CHECK (account_revision > 0),
            released_revision bigint CHECK (released_revision > 0 AND released_revision < account_revision),
            upstream_verified_at timestamptz NOT NULL CHECK (isfinite(upstream_verified_at)),
            PRIMARY KEY (account_id, order_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id)
                DEFERRABLE INITIALLY DEFERRED
        );
        CREATE TABLE public.payment_trial_conversions (
            account_id uuid NOT NULL, order_id uuid NOT NULL, shop_id uuid NOT NULL UNIQUE, trial_term_id uuid NOT NULL,
            paid_starts_at timestamptz NOT NULL CHECK (isfinite(paid_starts_at)),
            PRIMARY KEY (account_id, order_id, shop_id),
            FOREIGN KEY (account_id, order_id, shop_id) REFERENCES public.checkout_trial_sources(account_id, order_id, shop_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.paid_fresh_starts(account_id, order_id),
            FOREIGN KEY (shop_id) REFERENCES public.trial_history(shop_id)
        );
        CREATE TABLE public.payment_restoration_holds (
            account_id uuid NOT NULL, order_id uuid NOT NULL, attempt_id uuid UNIQUE, manual_receipt_id uuid UNIQUE,
            effective_at timestamptz NOT NULL CHECK (isfinite(effective_at)),
            held_at timestamptz NOT NULL CHECK (isfinite(held_at) AND held_at >= effective_at),
            account_revision bigint CHECK (account_revision > 0),
            restriction text CHECK (restriction IN ('Unrestricted', 'Trading', 'Security', 'Closed')),
            upstream_verified_at timestamptz CHECK (isfinite(upstream_verified_at)),
            CHECK ((attempt_id IS NULL) <> (manual_receipt_id IS NULL)),
            CHECK ((account_revision IS NULL AND restriction IS NULL AND upstream_verified_at IS NULL) OR
                (account_revision IS NOT NULL AND restriction IS NOT NULL AND upstream_verified_at IS NOT NULL)),
            PRIMARY KEY (account_id, order_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id),
            FOREIGN KEY (attempt_id) REFERENCES public.verified_payments(attempt_id),
            FOREIGN KEY (account_id, order_id, manual_receipt_id)
                REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id)
        );
        ALTER TABLE public.manual_payment_receipts DROP CONSTRAINT manual_payment_receipts_review_reason_check,
            ADD CHECK (review_reason IN ('DuplicateFunds', 'CoverageChanged', 'TermUnavailable', 'RestorationPending'));
        """;
}
