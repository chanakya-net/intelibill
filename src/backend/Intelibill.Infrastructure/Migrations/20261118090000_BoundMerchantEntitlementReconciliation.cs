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
                ADD COLUMN last_queued_at timestamptz NOT NULL DEFAULT statement_timestamp()
                    CHECK (isfinite(last_queued_at));
            UPDATE public.entitlement_reconciliation_requests
            SET last_queued_at = next_check_at - interval '1 hour',
                next_check_at = next_check_at - interval '59 minutes 5 seconds';
            """);
        migrationBuilder.Sql(MinuteScan);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(AddShopLicensingState.ReconciliationScan);
        migrationBuilder.Sql("""
            UPDATE public.entitlement_reconciliation_requests SET next_check_at = last_queued_at + interval '1 hour';
            ALTER TABLE public.entitlement_reconciliation_requests DROP COLUMN last_queued_at;
            ALTER TABLE public.entitlement_outbox DROP COLUMN ack_confirmed_at;
            """);
    }

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
