using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261118090000_PaymentConfirmations")]
public sealed partial class PaymentConfirmations : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string Receipt = "nullif(current_setting('app.manual_receipt_id', true), '')::uuid";
    private const string ManualOrder = "nullif(current_setting('app.manual_order_id', true), '')::uuid";

    private static readonly string[] SnapshotTables = ["verified_payments", "manual_payment_receipts", "checkout_orders",
        "checkout_order_shops", "checkout_order_acceptances", "billing_accounts", "payment_attempt_sessions", "payment_fulfilments"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(SnapshotFunction);
        foreach (var table in SnapshotTables)
            migrationBuilder.Sql($"ALTER TABLE public.{table} NO FORCE ROW LEVEL SECURITY");
        migrationBuilder.Sql("""
            INSERT INTO public.payment_confirmations (confirmation_id, account_id, order_id, attempt_id, manual_receipt_id, issued_at, facts)
            SELECT id, account_id, order_id, attempt_id, receipt_id, at,
                public.payment_confirmation_snapshot(id, account_id, order_id, attempt_id, receipt_id, at, 'Retained')
            FROM (SELECT gen_random_uuid() AS id, account_id, order_id, attempt_id, NULL::uuid AS receipt_id,
                    clock_timestamp() AS at FROM public.verified_payments
                UNION ALL SELECT gen_random_uuid(), account_id, order_id, NULL::uuid, receipt_id,
                    clock_timestamp() FROM public.manual_payment_receipts) receipts;
            """);
        foreach (var table in SnapshotTables)
            migrationBuilder.Sql($"ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY");
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(Guards);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.payment_confirmations IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.payment_confirmations NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.payment_confirmations) THEN
                RAISE EXCEPTION 'Retain issued payment confirmations on rollback' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP POLICY confirmation_party_read ON public.billing_accounts;
        DROP TABLE public.payment_confirmations;
        DROP FUNCTION public.guard_payment_confirmation();
        DROP FUNCTION public.payment_confirmation_snapshot(uuid,uuid,uuid,uuid,uuid,timestamptz,text);
        """);

    private const string Tables = """
        CREATE TABLE public.payment_confirmations (
            confirmation_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            attempt_id uuid UNIQUE REFERENCES public.verified_payments(attempt_id),
            manual_receipt_id uuid UNIQUE REFERENCES public.manual_payment_receipts(receipt_id),
            issued_at timestamptz NOT NULL CHECK (isfinite(issued_at)),
            facts jsonb NOT NULL CHECK (jsonb_typeof(facts) = 'object' AND facts->>'policy' = 'payment-confirmation/1'),
            CHECK ((attempt_id IS NULL) <> (manual_receipt_id IS NULL)),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id)
        );
        CREATE INDEX confirmation_order ON public.payment_confirmations(account_id, order_id, issued_at, confirmation_id);
        """;

    private static readonly string Policies = $"""
        ALTER TABLE public.payment_confirmations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_confirmations FORCE ROW LEVEL SECURITY;
        CREATE POLICY account_read ON public.payment_confirmations FOR SELECT USING (account_id = {Account});
        CREATE POLICY receipt_insert ON public.payment_confirmations FOR INSERT WITH CHECK (
            account_id = {Account} AND facts->'receipt'->>'issuanceBasis' = 'Verification' AND
            ((attempt_id = {Attempt} AND manual_receipt_id IS NULL) OR
             (manual_receipt_id = {Receipt} AND order_id = {ManualOrder} AND attempt_id IS NULL)));
        CREATE POLICY confirmation_party_read ON public.billing_accounts FOR SELECT USING (
            id = {Account} AND (EXISTS (SELECT FROM public.payment_attempts a WHERE a.account_id = id AND a.attempt_id = {Attempt})
                OR ({ManualOrder} IS NOT NULL AND nullif(current_setting('app.manual_admin_id', true), '') IS NOT NULL
                    AND nullif(current_setting('app.manual_session_id', true), '') IS NOT NULL)));
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.payment_confirmations
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_confirmations
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private const string Guards = """
        CREATE FUNCTION public.guard_payment_confirmation() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.facts IS DISTINCT FROM public.payment_confirmation_snapshot(NEW.confirmation_id, NEW.account_id,
                NEW.order_id, NEW.attempt_id, NEW.manual_receipt_id, NEW.issued_at, 'Verification') THEN
                RAISE EXCEPTION 'Confirmation must match verified receipt and accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_confirmation BEFORE INSERT ON public.payment_confirmations
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_confirmation();
        """;
}
