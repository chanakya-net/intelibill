using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261117160000_UnmatchedPaymentReviews")]
public sealed partial class UnmatchedPaymentReviews : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string Receipt = "nullif(current_setting('app.manual_receipt_id', true), '')::uuid";
    private const string Admin = "nullif(current_setting('app.manual_admin_id', true), '')::uuid";
    private const string Session = "nullif(current_setting('app.manual_session_id', true), '')::uuid";
    private const string ManualOrder = "nullif(current_setting('app.manual_order_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Cases);
        migrationBuilder.Sql(Decisions);
        migrationBuilder.Sql(Backfill);
        migrationBuilder.Sql(DecisionGuards);
        migrationBuilder.Sql(FulfilmentCompleteness(reviewedRecovery: true));
        Retain(migrationBuilder, "payment_review_decisions", DecisionRead,
            $"account_id = {Account} AND admin_id = {Admin} AND session_id = {Session} AND ({LiveBillingSession})");
        Retain(migrationBuilder, "unmatched_payments",
            $"account_id = {Account} AND (({Admin} IS NOT NULL AND {Session} IS NOT NULL) OR attempt_id = {Attempt})",
            $"account_id = {Account} AND (attempt_id = {Attempt} OR manual_receipt_id = {Receipt} AND order_id = {ManualOrder} AND {Admin} IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(FulfilmentCompleteness(reviewedRecovery: false));
        migrationBuilder.Sql("""
            LOCK TABLE public.unmatched_payments IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.unmatched_payments NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.unmatched_payments) THEN
                    RAISE EXCEPTION 'Payment review evidence must be retained' USING ERRCODE = '55000';
                END IF;
            END $$;
            DROP TABLE public.payment_review_decisions, public.unmatched_payments;
            DROP FUNCTION public.require_payment_review_recovery();
            DROP FUNCTION public.guard_unmatched_payment_source();
            """);
    }

    private static string DecisionRead => $"""
        account_id = {Account} AND (({Admin} IS NOT NULL AND {Session} IS NOT NULL) OR EXISTS (
            SELECT FROM public.unmatched_payments c WHERE c.account_id = payment_review_decisions.account_id
                AND c.case_id = payment_review_decisions.case_id AND c.attempt_id = {Attempt}))
        """;

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

    private const string Cases = """
        CREATE TABLE public.unmatched_payments (
            case_id uuid PRIMARY KEY, account_id uuid NOT NULL, order_id uuid NOT NULL,
            attempt_id uuid, manual_receipt_id uuid, inbox_id uuid REFERENCES public.gateway_inbox_events(inbox_id),
            classification text NOT NULL CHECK (classification IN ('ReceivingPinMismatch', 'CurrencyMismatch',
                'Underpayment', 'Overpayment', 'DuplicateFunds', 'CoverageChanged', 'TermUnavailable',
                'CaptureTimeUnproven', 'RestorationPending', 'LegacyNeedsReview')),
            expected_amount_paise bigint NOT NULL CHECK (expected_amount_paise > 0),
            expected_currency text NOT NULL CHECK (length(expected_currency) = 3),
            observed_amount_paise bigint NOT NULL CHECK (observed_amount_paise > 0),
            observed_currency text NOT NULL CHECK (length(observed_currency) = 3),
            provider_id text NOT NULL, receiving_account_id text NOT NULL, environment text NOT NULL,
            configuration_version text NOT NULL, provider_order_reference text, provider_payment_reference text,
            observed_at timestamptz NOT NULL CHECK (isfinite(observed_at)),
            captured_at timestamptz CHECK (isfinite(captured_at) AND captured_at <= observed_at),
            created_at timestamptz CHECK (isfinite(created_at) AND created_at <= observed_at),
            CHECK (num_nonnulls(attempt_id, manual_receipt_id) = 1),
            CHECK ((attempt_id IS NULL) = (provider_payment_reference IS NULL)),
            UNIQUE (account_id, case_id),
            FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id),
            FOREIGN KEY (account_id, order_id, manual_receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id)
        );
        CREATE UNIQUE INDEX unmatched_gateway_identity ON public.unmatched_payments
            (provider_id, receiving_account_id, environment, provider_payment_reference) WHERE attempt_id IS NOT NULL;
        CREATE UNIQUE INDEX unmatched_manual_identity ON public.unmatched_payments (manual_receipt_id) WHERE manual_receipt_id IS NOT NULL;
        CREATE INDEX unmatched_account_queue ON public.unmatched_payments (account_id, case_id);
        """;
}
