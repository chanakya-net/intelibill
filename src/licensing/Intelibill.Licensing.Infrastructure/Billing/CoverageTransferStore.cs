using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Billing;

/// <summary>
/// SQL-owned transfer proposals. A request freezes the named shop, both accounts, the treatment and digests of each
/// side's independent subscription and permit evidence; it writes no coverage, affiliation, grant or payment fact.
/// </summary>
internal sealed partial class CoverageTransferStore(
    LicensingDbContext db,
    ICapacityChangeQuoteStore terms,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock) : ICoverageTransferStore, ICoverageTransferReviewStore
{
    public async Task<ErrorOr<CoverageTransferPreviewV1>> PreviewAsync(Guid source, Guid term, Guid actor,
        PreviewCoverageTransferV1 query, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var facts = await ReadSourceAsync(source, term, actor, query.DestinationAccountId, query.ShopId, now, ct);
        if (facts.IsError) return facts.Errors;
        var f = facts.Value;
        if (query.CutoverAt is { } cutover && (cutover <= now || cutover >= f.PaidThroughAt))
            return CoverageTransferErrors.Invalid;
        return new CoverageTransferPreviewV1(source, query.DestinationAccountId, term, f.Shop.ShopId, f.Shop.Name,
            f.Shop.RequestId, f.PaidThroughAt, f.Owner.Revision, f.EvidenceDigest, query.Treatment, query.CutoverAt,
            f.Permits.OutstandingCount, f.Permits.ValidUntil, f.Permits.LastContactAt, now, true,
            CoverageTransfersV1.DisconnectedQueuesNotice, Expiry(now, query.CutoverAt, f.PaidThroughAt));
    }

    public async Task<ErrorOr<CoverageTransferV1>> RequestAsync(Guid source, Guid term, Guid actor,
        RequestCoverageTransferV1 command, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var reason = CoverageTransfersV1.NormalizeReason(command.Reason)!;
        var hash = CheckoutOrdersV1.Digest("coverage-transfer-request/1", source, term, command with { Reason = reason });
        if (await LookupRequestAsync(source, actor, command.OperationKey, ct) is { } prior)
        {
            if (!prior.Matches(await OwnerAsync(source, actor, ct))) return CoverageTransferErrors.AuthorityChanged;
            return prior.Hash == hash ? await ViewAsync(prior.Id, source, actor, now, ct) : BillingAccountErrors.PayloadMismatch;
        }

        var facts = await ReadSourceAsync(source, term, actor, command.DestinationAccountId, command.ShopId, now, ct);
        if (facts.IsError) return facts.Errors;
        var f = facts.Value;
        if (f.Shop.RequestId != command.ExpectedSourceAffiliationId || f.PaidThroughAt != command.ExpectedSourcePaidThroughAt ||
            f.Owner.Revision != command.ExpectedSourceOwnerRevision || f.EvidenceDigest != command.ExpectedEvidenceDigest)
            return CoverageTransferErrors.Changed;
        if (command.CutoverAt is { } cutover && (cutover <= now || cutover >= f.PaidThroughAt))
            return CoverageTransferErrors.Invalid;
        var expires = Expiry(now, command.CutoverAt, f.PaidThroughAt);
        if (expires <= now) return CoverageTransferErrors.Changed;

        await RetireObsoleteAsync(source, command.ShopId, now, ct);
        var id = Guid.NewGuid();
        var revision = await NextRevisionAsync(source, command.ShopId, ct);
        var digest = CoverageTransfersV1.TermsDigest(id, revision, source, command.DestinationAccountId, command.ShopId,
            f.Shop.RequestId, term, command.Treatment, command.CutoverAt, f.PaidThroughAt, f.EvidenceDigest, f.PermitDigest, expires);
        var inserted = await InsertAsync(id, revision, source, term, actor, command, reason, hash, digest, f, now, expires, ct);
        if (inserted != 1) return CoverageTransferErrors.Conflict;
        await db.Database.ExecuteSqlAsync($"SELECT public.coverage_transfer_freeze_evidence({id})", ct);
        return await ViewAsync(id, source, actor, now, ct);
    }

    public async Task<ErrorOr<CoverageTransferV1>> RecoverAsync(Guid source, Guid term, Guid actor, Guid key, CancellationToken ct)
    {
        if (await LookupRequestAsync(source, actor, key, ct) is not { } prior || prior.TermId != term)
            return CoverageTransferErrors.NotFound;
        return prior.Matches(await OwnerAsync(source, actor, ct))
            ? await ViewAsync(prior.Id, source, actor, EntitlementSnapshotsV1.Instant(clock), ct)
            : CoverageTransferErrors.AuthorityChanged;
    }

    // Seven days, never past the proposed cutover or the source's paid-through boundary. Half-open: gone at the instant.
    private static DateTimeOffset Expiry(DateTimeOffset now, DateTimeOffset? cutover, DateTimeOffset paidThrough)
    {
        var expires = now.AddDays(CoverageTransfersV1.ProposalLifetimeDays);
        if (cutover is { } c && c < expires) expires = c;
        return paidThrough < expires ? paidThrough : expires;
    }

    private async Task<ErrorOr<SourceFacts>> ReadSourceAsync(Guid source, Guid term, Guid actor, Guid destination,
        Guid shopId, DateTimeOffset now, CancellationToken ct)
    {
        if (await OwnerAsync(source, actor, ct) is not { } owner) return CoverageTransferErrors.PurposeDenied;
        var exists = await db.Database.SqlQuery<bool>(
            $"""SELECT public.coverage_transfer_account_exists({destination}) AS "Value" """).SingleAsync(ct);
        if (!exists) return CoverageTransferErrors.Invalid;
        var live = await terms.SourceAsync(source, term, now, ct);
        if (live is not { Basis: "Paid", Suspended: false, Closed: false, Superseded: false } || now >= live.EndsAt)
            return CoverageTransferErrors.NotFound;
        if (live.Shops.SingleOrDefault(s => s.ShopId == shopId) is not { } shop)
            return CoverageTransferErrors.Changed;

        var row = await db.Database.SqlQuery<Frozen>($"""
            SELECT public.term_effective_paid_through({source}, {term}) AS paid_through_at,
                public.coverage_transfer_evidence_digest({source}, {destination}) AS evidence_digest,
                public.coverage_transfer_permit_digest({source}, {shop.ShopId}, {shop.RequestId}, {now}) AS permit_digest,
                (SELECT name FROM public.billing_accounts WHERE id = {source}) AS source_name
            """).SingleAsync(ct);
        if (row.PaidThroughAt is not { } paidThrough || paidThrough <= now || row.SourceName is null)
            return CoverageTransferErrors.NotFound;
        var permits = await db.Database.SqlQuery<PermitObservation>($"""
            SELECT max(valid_until) FILTER (WHERE valid_until > {now}) AS valid_until, max(observed_at) AS last_contact_at,
                (count(*) FILTER (WHERE valid_until > {now}))::int AS outstanding_count
            FROM public.offline_device_observations
            WHERE account_id = {source} AND shop_id = {shop.ShopId} AND affiliation_request_id = {shop.RequestId}
            """).SingleAsync(ct);
        return new SourceFacts(owner, shop, paidThrough, row.EvidenceDigest, row.PermitDigest, row.SourceName, permits);
    }

    private Task<int> InsertAsync(Guid id, long revision, Guid source, Guid term, Guid actor, RequestCoverageTransferV1 c,
        string reason, string hash, string digest, SourceFacts f, DateTimeOffset now, DateTimeOffset expires, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_proposals (
                transfer_id, proposal_revision, source_account_id, source_account_name, destination_account_id,
                source_term_id, shop_id, shop_name, source_affiliation_id, treatment, cutover_at, source_paid_through_at,
                observed_permit_count, observed_permit_deadline, last_contact_at, observed_at, permit_digest,
                evidence_digest, terms_digest, request_reason, requester_id, requester_member_id, requester_revision,
                operation_key, request_hash, created_at, expires_at, state)
            VALUES ({id}, {revision}, {source}, {f.SourceName}, {c.DestinationAccountId}, {term}, {f.Shop.ShopId},
                {f.Shop.Name}, {f.Shop.RequestId}, {c.Treatment}, {c.CutoverAt}::timestamptz, {f.PaidThroughAt},
                {f.Permits.OutstandingCount}, {f.Permits.ValidUntil}::timestamptz, {f.Permits.LastContactAt}::timestamptz,
                {now}, {f.PermitDigest}, {f.EvidenceDigest}, {digest}, {reason}, {actor}, {f.Owner.Id}, {f.Owner.Revision},
                {c.OperationKey}, {hash}, {now}, {expires}, 'Open')
            ON CONFLICT DO NOTHING
            """, ct);

    // An expired or stale open proposal must not hold the named shop's single open slot forever.
    private Task<int> RetireObsoleteAsync(Guid source, Guid shop, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_transfer_proposals p
            SET state = 'Retired', retired_reason = public.coverage_transfer_blocker(p.transfer_id, {now}), retired_at = {now}
            WHERE p.source_account_id = {source} AND p.shop_id = {shop} AND p.state = 'Open'
                AND public.coverage_transfer_blocker(p.transfer_id, {now}) IS NOT NULL
            """, ct);

    private Task<long> NextRevisionAsync(Guid source, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<long>($"""
            SELECT coalesce(max(proposal_revision), 0) + 1 AS "Value" FROM public.coverage_transfer_proposals
            WHERE source_account_id = {source} AND shop_id = {shop}
            """).SingleAsync(ct);

    private Task<PriorRequest?> LookupRequestAsync(Guid source, Guid actor, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<PriorRequest>($"""
            SELECT transfer_id AS id, request_hash AS hash, source_term_id AS term_id, requester_member_id,
                requester_revision::bigint AS requester_revision
            FROM public.coverage_transfer_proposals
            WHERE source_account_id = {source} AND requester_id = {actor} AND operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    private Task<LiveOwner?> OwnerAsync(Guid account, Guid actor, CancellationToken ct) =>
        db.Database.SqlQuery<LiveOwner>($"""
            SELECT id, revision::bigint AS revision FROM public.billing_account_members
            WHERE account_id = {account} AND merchant_user_id = {actor} AND role = 1 AND status = 1
            """).SingleOrDefaultAsync(ct);

    private sealed record LiveOwner(Guid Id, long Revision);
    private sealed record PriorRequest(Guid Id, string Hash, Guid TermId, Guid RequesterMemberId, long RequesterRevision)
    {
        internal bool Matches(LiveOwner? owner) => owner?.Id == RequesterMemberId && owner.Revision == RequesterRevision;
    }
    private sealed record Frozen(DateTimeOffset? PaidThroughAt, string EvidenceDigest, string PermitDigest, string? SourceName);
    private sealed record PermitObservation(DateTimeOffset? ValidUntil, DateTimeOffset? LastContactAt, int OutstandingCount);
    private sealed record SourceFacts(LiveOwner Owner, CapacityChangeSourceShop Shop, DateTimeOffset PaidThroughAt,
        string EvidenceDigest, string PermitDigest, string SourceName, PermitObservation Permits);
}
