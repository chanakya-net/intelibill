using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261117110000_ManualPaymentReceipts")]
public sealed class ManualPaymentReceipts : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Admin = "nullif(current_setting('app.manual_admin_id', true), '')::uuid";
    private const string Session = "nullif(current_setting('app.manual_session_id', true), '')::uuid";
    private const string Order = "nullif(current_setting('app.manual_order_id', true), '')::uuid";
    private const string Receipt = "nullif(current_setting('app.manual_receipt_id', true), '')::uuid";
    private const string ScopedOrder = $"account_id = {Account} AND order_id = {Order} AND {Admin} IS NOT NULL AND {Session} IS NOT NULL";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "checkout_orders", "checkout_order_shops", "checkout_order_acceptances" })
            migrationBuilder.Sql($"CREATE POLICY manual_order_read ON public.{table} FOR SELECT USING ({ScopedOrder})");
        foreach (var table in new[] { "payment_fulfilment_shops", "payment_fulfilment_deliveries" })
            migrationBuilder.Sql(InsertPolicy(table, "order_id"));
        foreach (var table in new[] { "subscription_coverage_terms", "subscription_term_facts" })
            migrationBuilder.Sql(InsertPolicy(table, "term_id",
                table == "subscription_term_facts" ? "AND basis = 'Paid' AND source_reference = 'payment-fulfilment/1'" : ""));
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql(Completeness("""
            (NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_events e
                WHERE e.attempt_id = NEW.attempt_id AND e.account_id = NEW.account_id
                    AND e.order_id = NEW.order_id AND e.kind = 'OrderFulfilled'))
            OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
                WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id
                    AND r.order_id = NEW.order_id AND r.term_id = NEW.term_id AND r.review_reason IS NULL))
            """));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.manual_payment_receipts IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.manual_payment_receipts NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.manual_payment_receipts) THEN
                    RAISE EXCEPTION 'Manual payment evidence exists' USING ERRCODE = '55000';
                END IF;
            END $$;
            """);
        foreach (var table in new[] { "checkout_orders", "checkout_order_shops", "checkout_order_acceptances" })
            migrationBuilder.Sql($"DROP POLICY manual_order_read ON public.{table}");
        foreach (var table in new[] { "payment_fulfilments", "payment_fulfilment_shops", "payment_fulfilment_deliveries",
            "subscription_coverage_terms", "subscription_term_facts" })
            migrationBuilder.Sql($"DROP POLICY manual_insert ON public.{table}");
        migrationBuilder.Sql(Completeness("""
            NOT EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = NEW.attempt_id AND e.kind = 'OrderFulfilled')
            """));
        migrationBuilder.Sql("""
            ALTER TABLE public.payment_fulfilments DROP CONSTRAINT payment_fulfilment_one_source,
                DROP COLUMN manual_receipt_id, ALTER COLUMN attempt_id SET NOT NULL;
            DROP TABLE public.manual_payment_receipts;
            DROP FUNCTION public.guard_manual_payment_receipt(), public.require_complete_manual_payment_receipt();
            """);
    }

    private static string InsertPolicy(string table, string identity, string additionalPredicate = "") => $"""
        CREATE POLICY manual_insert ON public.{table} FOR INSERT WITH CHECK (
            account_id = {Account} {additionalPredicate} AND EXISTS (SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = {table}.account_id AND f.{identity} = {table}.{identity}
                    AND f.order_id = {Order} AND f.manual_receipt_id = {Receipt}));
        """;

    private static readonly string Tables = $$"""
        CREATE TABLE public.manual_payment_receipts (
            receipt_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            admin_id uuid NOT NULL,
            session_id uuid NOT NULL,
            mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
            received_at timestamptz NOT NULL CHECK (isfinite(received_at)),
            verified_at timestamptz NOT NULL CHECK (isfinite(verified_at) AND received_at <= verified_at),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            currency text NOT NULL CHECK (currency = 'INR'),
            receiving_account_id varchar(80) NOT NULL CHECK (length(receiving_account_id) > 0),
            configuration_version varchar(80) NOT NULL CHECK (length(configuration_version) > 0),
            evidence_policy_version varchar(80) NOT NULL CHECK (length(evidence_policy_version) > 0),
            environment text NOT NULL CHECK (environment IN ('Development', 'Test', 'Live')),
            reference_digest text NOT NULL CHECK (reference_digest ~ '^[a-f0-9]{64}$'),
            protected_payload text NOT NULL CHECK (length(protected_payload) BETWEEN 1 AND 16384),
            quote_revision integer NOT NULL CHECK (quote_revision > 0),
            snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[a-f0-9]{64}$'),
            review_reason text CHECK (review_reason IN ('DuplicateFunds', 'CoverageChanged', 'TermUnavailable')),
            term_id uuid,
            CHECK ((term_id IS NULL) <> (review_reason IS NULL)),
            UNIQUE (account_id, operation_key),
            UNIQUE (account_id, order_id, receipt_id),
            CONSTRAINT manual_receiving_reference UNIQUE (receiving_account_id, environment, reference_digest)
        );
        CREATE INDEX manual_receipt_order ON public.manual_payment_receipts(account_id, order_id, verified_at);
        ALTER TABLE public.manual_payment_receipts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.manual_payment_receipts FORCE ROW LEVEL SECURITY;
        CREATE POLICY account_read ON public.manual_payment_receipts FOR SELECT USING (account_id = {{Account}});
        CREATE POLICY manual_insert ON public.manual_payment_receipts FOR INSERT WITH CHECK (
            {{ScopedOrder}} AND admin_id = {{Admin}} AND session_id = {{Session}} AND receipt_id = {{Receipt}}
            AND EXISTS (SELECT FROM public.admin_sessions s JOIN public.admin_identities a ON a.id = s.admin_id
                WHERE s.id = manual_payment_receipts.session_id AND a.id = manual_payment_receipts.admin_id
                    AND s.revoked_at IS NULL AND s.mfa_verified_at = manual_payment_receipts.mfa_verified_at
                    AND a.mfa_enrolled_at IS NOT NULL AND a.deactivated_at IS NULL AND (a.capabilities & 2) = 2));
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.manual_payment_receipts
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.manual_payment_receipts
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        ALTER TABLE public.payment_fulfilments ALTER COLUMN attempt_id DROP NOT NULL,
            ADD COLUMN manual_receipt_id uuid UNIQUE,
            ADD FOREIGN KEY (account_id, order_id, manual_receipt_id)
                REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id),
            ADD CONSTRAINT payment_fulfilment_one_source CHECK ((attempt_id IS NULL) <> (manual_receipt_id IS NULL));
        CREATE POLICY manual_insert ON public.payment_fulfilments FOR INSERT WITH CHECK (
            {{ScopedOrder}} AND manual_receipt_id = {{Receipt}} AND attempt_id IS NULL);
        """;

    private const string Guards = """
        CREATE FUNCTION public.guard_manual_payment_receipt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q JOIN public.checkout_order_acceptances a USING (account_id, order_id)
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment'
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND a.accepted_at < q.expires_at) THEN
                RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_manual_payment_receipt BEFORE INSERT ON public.manual_payment_receipts
            FOR EACH ROW EXECUTE FUNCTION public.guard_manual_payment_receipt();

        CREATE FUNCTION public.require_complete_manual_payment_receipt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.term_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.manual_receipt_id = NEW.receipt_id
                    AND f.term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Manual receipt fulfilment is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER manual_receipt_complete AFTER INSERT ON public.manual_payment_receipts
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_complete_manual_payment_receipt();
        """;

    private static string Completeness(string missingAudit) => $"""
        CREATE OR REPLACE FUNCTION public.require_complete_payment_fulfilment() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                    JOIN public.subscription_term_facts t ON t.account_id = q.account_id AND t.term_id = NEW.term_id
                    JOIN public.subscription_coverage_terms c ON c.account_id = q.account_id AND c.term_id = NEW.term_id
                    WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND t.basis = 'Paid'
                        AND t.price_paise = q.amount_paise AND c.capacity = (q.snapshot->>'capacity')::int
                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = q.shop_count)
                OR ({missingAudit}) THEN
                RAISE EXCEPTION 'Payment fulfilment evidence is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        """;
}
