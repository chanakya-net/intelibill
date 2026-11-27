using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed partial class CapacityReservationAcknowledgements
{
    // The account lock, retained inbox receipt and decision update share this transaction.
    // Derive every money field from the winning receipt. A merchant acknowledgement contains no payment facts.
    private async Task RecordReviewAsync(CapacityReservationV1 c, string reason, CancellationToken ct)
    {
        var cases = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.unmatched_payments (case_id,account_id,order_id,classification,attempt_id,manual_receipt_id,
                expected_amount_paise,expected_currency,observed_amount_paise,observed_currency,provider_id,receiving_account_id,
                environment,configuration_version,provider_order_reference,provider_payment_reference,observed_at,captured_at)
            SELECT {Guid.NewGuid()},i.account_id,i.order_id,{reason},i.attempt_id,i.receipt_id,
                coalesce(v.amount_paise,r.amount_paise),coalesce(v.currency,r.currency),
                coalesce(v.amount_paise,r.amount_paise),coalesce(v.currency,r.currency),
                coalesce(v.provider_id,'manual'),coalesce(v.provider_account_id,r.receiving_account_id),
                coalesce(v.environment,r.environment),coalesce(v.configuration_version,r.configuration_version),
                s.provider_order_reference,v.provider_payment_reference,greatest(v.verified_at,v.captured_at,r.verified_at),
                CASE WHEN v.capture_time_basis='ProviderCapture' THEN v.captured_at ELSE r.received_at END
            FROM public.capacity_increases i LEFT JOIN public.verified_payments v USING (account_id,order_id,attempt_id)
            LEFT JOIN public.payment_attempt_sessions s ON s.account_id=i.account_id AND s.attempt_id=i.attempt_id
            LEFT JOIN public.manual_payment_receipts r ON r.account_id=i.account_id AND r.receipt_id=i.receipt_id
            WHERE i.account_id={c.AccountId} AND i.order_id={c.OrderId} AND i.state='NeedsReview'
            """, ct);
        if (cases != 1) throw new InvalidOperationException("Missing capacity review source.");
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.payment_events(event_id,account_id,order_id,attempt_id,kind,recorded_at)
            SELECT {Guid.NewGuid()},account_id,order_id,attempt_id,'PaidNeedsReview',{clock.GetUtcNow()}
            FROM public.capacity_increases WHERE account_id={c.AccountId} AND order_id={c.OrderId} AND attempt_id IS NOT NULL
            """, ct);
    }
}
