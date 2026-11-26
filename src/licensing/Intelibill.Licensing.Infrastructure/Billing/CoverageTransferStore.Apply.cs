using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageTransferStore
{
    internal async Task<string?> ApplicationBlockerAsync(TransferCommandV1 c, Guid[] events,
        DateTimeOffset approvedAt, CancellationToken ct)
    {
        // Approval made the schedule durable before expiry. Use its decision instant only for the proposal
        // expiry predicate. All membership, roster, consent and term rows are read live under both locks.
        var proposal = await ProposalAsync(c.TransferId, approvedAt, ct);
        if (proposal?.TermsDigest != c.TermsDigest) return "ProposalChanged";
        if (proposal.Blocker is not null) return proposal.Blocker;
        var rows = await ConsentsAsync(c.TransferId, ct);
        if (!Purposes.Select(p => Current(p, rows)?.EventId).SequenceEqual(events.Select(e => (Guid?)e)) ||
            Purposes.Any(p => Status(p, rows).Status != CoverageTransfersV1.Accepted)) return "ConsentChanged";
        var now = EntitlementSnapshotsV1.Instant(clock);
        if (now >= c.PaidThroughAt && c.Treatment == CoverageTransfersV1.ReviewedAllocation) return "AllocationElapsed";
        var live = await terms.SourceAsync(c.SourceAccountId, c.TermId, now, ct);
        if (live is not { Basis: "Paid", CompleteEvidence: true, Closed: false, Suspended: false, Superseded: false } ||
            live.MemberAllowance != c.MemberAllowance || !live.Shops.Any(s => s.RequestId == c.AffiliationId))
            return "SourceChanged";
        await SetAccountAsync(c.DestinationAccountId, ct);
        var restricted = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM public.active_operational_restrictions WHERE account_id={c.DestinationAccountId}
                AND (shop_id IS NULL OR shop_id={c.ShopId}) AND effective_at<={now})
                OR EXISTS(SELECT FROM public.operational_closures WHERE account_id={c.DestinationAccountId} AND shop_id IS NULL)
                OR EXISTS(SELECT FROM public.active_operational_closure_targets WHERE account_id={c.DestinationAccountId}
                    AND (shop_id IS NULL OR shop_id={c.ShopId})) AS "Value"
            """).SingleAsync(ct);
        await SetAccountAsync(c.SourceAccountId, ct);
        return restricted ? "DestinationRestricted" : null;
    }
}
