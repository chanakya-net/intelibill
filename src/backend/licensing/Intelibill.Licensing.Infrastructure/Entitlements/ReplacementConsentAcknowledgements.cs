using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed class ReplacementConsentAcknowledgements(LicensingDbContext db, ICapacityChangeQuoteStore terms,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock)
{
    internal async Task<bool> ApplyAsync(ReplacementConsentV1 receipt, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var row = await db.Database.SqlQuery<Request>($"""
            SELECT requester_id,requester_revision,source_hash,created_at,expires_at,state FROM public.coverage_replacements
            WHERE replacement_id={receipt.ReplacementId} AND account_id={receipt.AccountId} AND term_id={receipt.TermId}
                AND incoming_shop_id={receipt.ShopId} AND incoming_affiliation_id={receipt.AffiliationRequestId}
                AND effective_at={receipt.EffectiveAt} AND member_allowance={receipt.MemberAllowance}
            """).SingleOrDefaultAsync(ct);
        if (row is null || receipt.RecordedAt < row.CreatedAt || receipt.RecordedAt > now || (receipt.Kind != "Revoked" && receipt.RecordedAt >= row.ExpiresAt))
            return false;
        if (row.State == "Blocked") return true; // Revocation may arrive ahead of acceptance.
        if (receipt.Kind == "Revoked")
            return await db.Database.ExecuteSqlAsync($"""
                UPDATE public.coverage_replacements SET revoke_event={receipt.EventId}, reason='ConsentRevoked',
                    state=CASE WHEN state='Applied' THEN state ELSE 'Blocked' END
                WHERE replacement_id={receipt.ReplacementId} AND revoke_event IS NULL
                """, ct) == 1;
        if (row.State != "AwaitingConsent") return false;
        var source = await terms.SourceAsync(receipt.AccountId, receipt.TermId, now, ct);
        var valid = source is { Closed: false, Superseded: false } && source.Hash() == row.SourceHash &&
            now < row.ExpiresAt && receipt.ActiveMembers <= receipt.MemberAllowance && await CurrentAsync(row, receipt, now, ct);
        return await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacements SET consent_event={receipt.EventId},
                state={(valid ? "Scheduled" : "Blocked")},reason={(valid ? null : "SourceChanged")}::text
            WHERE replacement_id={receipt.ReplacementId} AND state='AwaitingConsent'
            """, ct) == 1;
    }

    private async Task<bool> CurrentAsync(Request row, ReplacementConsentV1 c, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.billing_actor_id',{row.RequesterId.ToString("D")},true)", ct);
        return await db.Database.SqlQuery<bool>($"""
            SELECT public.replacement_term_available({c.AccountId},{c.TermId})
                AND public.slot_shop_eligible({c.AccountId},{c.ShopId},{now},'infinity')
                AND EXISTS(SELECT FROM public.billing_account_members WHERE account_id={c.AccountId}
                    AND merchant_user_id={row.RequesterId} AND status=1 AND role=1 AND revision={row.RequesterRevision})
                AND NOT EXISTS(SELECT FROM public.checkout_orders q WHERE q.account_id={c.AccountId} AND q.term_id={c.TermId}
                    AND q.snapshot->>'startRule'='PriorPaidThrough'
                    AND (EXISTS(SELECT FROM public.payment_attempts a WHERE a.order_id=q.order_id AND NOT EXISTS(
                        SELECT FROM public.payment_events e WHERE e.attempt_id=a.attempt_id AND e.kind='CheckoutFailed'))
                    OR EXISTS(SELECT FROM public.verified_payments v WHERE v.order_id=q.order_id)
                    OR EXISTS(SELECT FROM public.manual_payment_receipts m WHERE m.order_id=q.order_id))) AS "Value"
            """).SingleAsync(ct);
    }

    private sealed record Request(Guid RequesterId, int RequesterRevision, string SourceHash,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string State);
}
