using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

internal static class CoverageExceptionActivationGate
{
    internal static Task<bool> ActivatedAsync(ApplicationDbContext db, Guid exceptionId, string leg,
        Guid accountId, Guid shopId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.merchant_coverage_exception_activations
                WHERE exception_id = {exceptionId} AND leg = {leg}
                    AND account_id = {accountId} AND shop_id = {shopId}) AS "Value"
            """).SingleAsync(ct);

    internal static async Task<string> CheckAsync(ApplicationDbContext db, EntitlementSnapshotV1 snapshot,
        Guid exceptionId, string leg, long generation, DateTimeOffset now, CancellationToken ct)
    {
        if (leg == CoverageReplacementExceptionsV1.OutgoingLeg)
        {
            var validOutgoing = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT FROM public.merchant_coverage_exception_cutoffs c
                    WHERE c.exception_id = {exceptionId} AND c.generation = {generation}
                        AND c.account_id = {snapshot.AccountId} AND c.shop_id = {snapshot.ShopId}
                        AND c.affiliation_request_id = {snapshot.AffiliationRequestId}
                        AND ({snapshot.Shop!.AccessEndsAt}::timestamptz IS NULL OR {snapshot.Shop.AccessEndsAt}::timestamptz <= c.cutoff_at)) AS "Value"
                """).SingleAsync(ct);
            if (!validOutgoing) return "Invalid";
            return await ActivatedAsync(db, exceptionId, leg, snapshot.AccountId, snapshot.ShopId, ct) ? "Activated" : "Ready";
        }

        if (await ActivatedAsync(db, exceptionId, leg, snapshot.AccountId, snapshot.ShopId, ct))
            return "Activated";

        var consentAndDecision = await db.Database.SqlQuery<DecisionCheck?>($"""
            SELECT d.overlap_disposition, d.requested_early_start_at, d.paid_cutover_at
            FROM public.merchant_coverage_exception_decisions d
            WHERE d.exception_id = {exceptionId} AND d.generation = {generation} AND d.outcome = 'Consumed'
                AND d.account_id = {snapshot.AccountId} AND d.incoming_shop_id = {snapshot.ShopId}
                AND d.incoming_affiliation_id = {snapshot.AffiliationRequestId}
                AND EXISTS (SELECT FROM public.coverage_exception_consent_events e
                    JOIN public.shop_memberships m ON m.shop_id = e.shop_id AND m.id = e.membership_id AND m.user_id = e.actor_id
                    WHERE e.exception_id = {exceptionId} AND e.leg = 'Incoming' AND e.kind = 'Accepted'
                        AND e.shop_id = {snapshot.ShopId} AND e.account_id = {snapshot.AccountId}
                        AND e.affiliation_request_id = {snapshot.AffiliationRequestId}
                        AND e.member_allowance = {snapshot.Shop!.MemberAllowance}
                        AND m.authority_version = e.authority_version AND m.role = 'Owner' AND m.status = 'Active'
                        AND NOT EXISTS (SELECT FROM public.coverage_exception_consent_events v
                            WHERE v.exception_id = e.exception_id AND v.leg = 'Incoming' AND v.kind = 'Revoked')
                        AND (SELECT count(*) FROM public.shop_memberships WHERE shop_id = e.shop_id AND status = 'Active') <= e.member_allowance)
            """).SingleOrDefaultAsync(ct);
        if (consentAndDecision is null) return "Invalid";

        if (leg == CoverageReplacementExceptionsV1.IncomingCourtesyLeg)
        {
            if (consentAndDecision.OverlapDisposition != CoverageReplacementExceptionsV1.CourtesyBridgeDisposition
                || snapshot.Shop!.Basis != "Complimentary"
                || snapshot.Shop.StartsAt != consentAndDecision.RequestedEarlyStartAt
                || snapshot.Shop.AccessEndsAt != consentAndDecision.PaidCutoverAt)
                return "Invalid";
            return now < consentAndDecision.RequestedEarlyStartAt ? "Waiting" : "Ready";
        }

        if (leg != CoverageReplacementExceptionsV1.IncomingPaidLeg || snapshot.Shop!.Basis != "Paid")
            return "Invalid";

        var released = await db.Database.SqlQuery<bool>($"""
            SELECT public.exception_outgoing_released({exceptionId}, {generation}, {now}) AS "Value"
            """).SingleAsync(ct);
        return released ? "Ready" : "Waiting";
    }

    internal static Task<int> RecordAsync(ApplicationDbContext db, EntitlementSnapshotV1 snapshot,
        Guid exceptionId, string leg, long generation, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.merchant_coverage_exception_activations (
                exception_id, leg, generation, shop_id, account_id, event_id, shop_revision, activated_at)
            VALUES ({exceptionId}, {leg}, {generation}, {snapshot.ShopId}, {snapshot.AccountId},
                {snapshot.EventId}, {snapshot.Shop!.Revision}, {now})
            ON CONFLICT (exception_id, leg) DO NOTHING
            """, ct);

    private sealed record DecisionCheck(string OverlapDisposition, DateTimeOffset RequestedEarlyStartAt,
        DateTimeOffset PaidCutoverAt);
}
