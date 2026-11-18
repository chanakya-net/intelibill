using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118090000_BoundMerchantEntitlementReconciliation")]
public sealed class BoundMerchantEntitlementReconciliation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_outbox
                ADD COLUMN ack_confirmed_at timestamptz CHECK (isfinite(ack_confirmed_at));
            ALTER TABLE public.entitlement_reconciliation_requests
                ADD COLUMN last_queued_at timestamptz CHECK (isfinite(last_queued_at));
            UPDATE public.entitlement_reconciliation_requests
            SET last_queued_at = next_check_at - interval '1 hour',
                next_check_at = next_check_at - interval '59 minutes 5 seconds';
            ALTER TABLE public.entitlement_reconciliation_requests ALTER COLUMN last_queued_at SET NOT NULL;
            """);
        migrationBuilder.Sql(BoundSchedule);
        migrationBuilder.Sql(ProofIssuance);
        migrationBuilder.Sql(MinuteScan);
    }

    // The fence reads the proof function, so this step must not commit its removal ahead of the
    // predecessor's refusal. Refuse here first, exactly as that migration does.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.shop_license_states, public.license_grant_evidence, public.entitlement_messages
                IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.shop_license_states NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.license_grant_evidence NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.entitlement_messages NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.license_grant_evidence) OR EXISTS (SELECT FROM public.shop_license_states)
                    OR EXISTS (SELECT FROM public.entitlement_messages) THEN
                    RAISE EXCEPTION 'Retained entitlement evidence prevents downgrade' USING ERRCODE = '55000';
                END IF;
            END $$;
            ALTER TABLE public.shop_license_states FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.license_grant_evidence FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.entitlement_messages FORCE ROW LEVEL SECURITY;
            DROP FUNCTION public.merchant_licence_proof_issuance(uuid);
            ALTER POLICY license_grant_evidence_shop_isolation ON public.license_grant_evidence
                USING (shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
            DROP TRIGGER bound_entitlement_reconciliation_schedule ON public.entitlement_reconciliation_requests;
            DROP FUNCTION public.bound_entitlement_reconciliation_schedule();
            """);
        migrationBuilder.Sql(AddShopLicensingState.ReconciliationScan);
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_reconciliation_requests DROP COLUMN last_queued_at;
            ALTER TABLE public.entitlement_outbox DROP COLUMN ack_confirmed_at;
            """);
    }

    // A previous-release worker still writes "queued + 1 hour" and knows nothing of last_queued_at. Its INSERT
    // leaves the column null. Its upsert keeps the old value, and EXCLUDED already carries the bounded time.
    private const string BoundSchedule = """
        CREATE FUNCTION public.bound_entitlement_reconciliation_schedule() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.last_queued_at IS NULL THEN
                NEW.last_queued_at := NEW.next_check_at - interval '1 hour';
            ELSIF TG_OP = 'UPDATE' AND NEW.event_id <> OLD.event_id AND NEW.last_queued_at = OLD.last_queued_at THEN
                NEW.last_queued_at := NEW.next_check_at - interval '55 seconds';
            END IF;
            NEW.next_check_at := least(NEW.next_check_at, NEW.last_queued_at + interval '55 seconds');
            RETURN NEW;
        END $$;
        CREATE TRIGGER bound_entitlement_reconciliation_schedule
            BEFORE INSERT OR UPDATE ON public.entitlement_reconciliation_requests
            FOR EACH ROW EXECUTE FUNCTION public.bound_entitlement_reconciliation_schedule();
        """;

    // Returns signing times only, for the active shop's own heads, so the runtime needs no separate grant.
    // The owner clause matches the head tables and lets this definer reach a sibling's evidence row without
    // exposing that row to any shop session.
    private const string ProofIssuance = """
        DO $$ BEGIN
            EXECUTE format($policy$ALTER POLICY license_grant_evidence_shop_isolation ON public.license_grant_evidence
                USING (shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid OR current_user = %L)$policy$,
                current_user);
        END $$;
        CREATE FUNCTION public.merchant_licence_proof_issuance(p_shop_id uuid)
        RETURNS TABLE (shop_issued_at timestamptz, account_issued_at timestamptz)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT se.issued_at, ae.issued_at
            FROM public.shop_license_states s
            LEFT JOIN public.license_grant_evidence se ON se.event_id = s.evidence_id AND se.shop_id = s.shop_id
            LEFT JOIN public.account_license_restrictions a ON a.account_id = s.account_id
            LEFT JOIN public.license_grant_evidence ae ON ae.event_id = a.evidence_id AND ae.account_id = a.account_id
            WHERE s.shop_id = p_shop_id
                AND p_shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
        $$;
        """;

    private static string MinuteScan => $$"""
        CREATE OR REPLACE FUNCTION public.due_merchant_entitlement_reconciliation(p_now timestamptz)
        RETURNS TABLE (account_id uuid, shop_id uuid, affiliation_request_id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT e.account_id, e.shop_id, e.request_id FROM public.shop_affiliation_consent_events e
            LEFT JOIN public.entitlement_reconciliation_requests r USING (shop_id)
            LEFT JOIN public.entitlement_outbox o ON o.event_id = r.event_id
            LEFT JOIN public.shop_license_states s ON s.shop_id = e.shop_id AND s.account_id = e.account_id
                AND s.affiliation_request_id = e.request_id
            WHERE e.kind = 'Accepted' AND (r.shop_id IS NULL OR (o.status IN ('Delivered', 'DeadLetter') AND
                (r.next_check_at <= p_now OR s.next_boundary > r.last_queued_at AND s.next_boundary <= p_now)))
                AND {{LeasedHttpOutboxSchema.Unscoped}}
                AND NOT EXISTS (SELECT FROM public.shop_affiliation_delivery_receipts f
                    WHERE f.event_id = e.id AND f.status = 'NeedsReview')
            ORDER BY r.next_check_at NULLS FIRST, e.shop_id LIMIT 20
        $$;
        """;
}
