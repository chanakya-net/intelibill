using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageReplacementExceptionStore(
    LicensingDbContext db,
    ICapacityChangeQuoteStore terms,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock,
    TimeProvider deliveryClock,
    ILogger<CoverageReplacementExceptionStore> logger) : ICoverageReplacementExceptionStore
{
    public async Task<ErrorOr<CoverageReplacementExceptionPreviewV1>> ReadCustomerAsync(
        Guid account, Guid term, int page, CancellationToken ct)
    {
        const int size = CoverageReplacementExceptionsV1.PageSize;
        if (page is < 1 or > int.MaxValue / size) return CoverageReplacementExceptionErrors.Invalid;
        var offset = (page - 1) * size;
        var now = EntitlementSnapshotsV1.Instant(clock);
        var source = await terms.SourceAsync(account, term, now, ct);
        if (source is not { Basis: "Paid", MemberAllowance: { } members })
            return CoverageReplacementExceptionErrors.NotFound;

        var covered = source.Shops.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.ShopId)
            .Skip(offset).Take(size).ToArray();
        var obs = await ReadObservedPermitsAsync(account, covered.Select(s => s.ShopId).ToArray(), now, ct);
        var eligible = await db.Database.SqlQuery<SlotShopV1>($"""
            SELECT shop_id, shop_name FROM public.current_shop_affiliations WHERE account_id = {account}
                AND public.slot_shop_eligible({account}, shop_id, {now}, public.term_effective_access_end({account}, {term}))
            ORDER BY shop_name, shop_id LIMIT {size + 1} OFFSET {offset}
            """).ToArrayAsync(ct);
        var items = await CustomerRows(account, now).Where(r => r.TermId == term)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.ExceptionId)
            .Skip(offset).Take(size + 1).ToArrayAsync(ct);
        var canRequest = ScheduledChangeValidator.ValidateSource(source, now) is null
            && !source.Suspended && now < source.EndsAt
            && await db.Database.SqlQuery<bool>($"""
                SELECT public.replacement_term_available({account}, {term}) AND EXISTS (
                    SELECT FROM public.billing_account_members
                    WHERE account_id = {account} AND merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                        AND role = 1 AND status = 1) AS "Value"
                """).SingleAsync(ct);

        var coveredDtos = covered.Select(s =>
        {
            var seen = obs.GetValueOrDefault((s.ShopId, s.RequestId));
            return new EarlyReplacementCoveredShopV1(s.ShopId, s.Name, seen?.ValidUntil, seen?.LastContactAt,
                seen?.LastSynchronizedAt, seen?.OutstandingCount ?? 0);
        }).ToArray();

        return new CoverageReplacementExceptionPreviewV1(term, source.Capacity, members, source.EndsAt, now, canRequest,
            CoverageReplacementExceptionsV1.DisconnectedQueuesNotice, coveredDtos, [.. eligible.Take(size)],
            [.. items.Take(size)], items.Length > size || source.Shops.Length - offset > size || eligible.Length > size);
    }

    public async Task<ErrorOr<CoverageReplacementExceptionV1>> RequestAsync(Guid account, Guid term, Guid actor,
        RequestCoverageReplacementExceptionV1 command, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var reason = CoverageReplacementExceptionsV1.NormalizeReason(command.Reason);
        if (reason is null) return CoverageReplacementExceptionErrors.Invalid;

        var hash = CheckoutOrdersV1.Digest("coverage-replacement-exception-request/1", term, command with { Reason = reason });
        var previous = await LookupCustomerOperationAsync(account, actor, command.OperationKey, ct);
        if (previous is not null)
            return previous.Hash == hash
                ? await CustomerRows(account, now).SingleAsync(r => r.ExceptionId == previous.Id, ct)
                : BillingAccountErrors.PayloadMismatch;

        var source = await terms.SourceAsync(account, term, now, ct);
        if (source is null || source.Suspended || now >= source.EndsAt ||
            ScheduledChangeValidator.ValidateSource(source, now, command.ObservedCapacity,
                command.ObservedMemberAllowance, command.ObservedNormalRenewalAt) is not null)
            return CoverageReplacementExceptionErrors.Changed;

        if (command.RequestedEarlyStartAt < source.StartsAt || command.ProposedPaidCutoverAt <= now
            || command.ProposedPaidCutoverAt >= source.EndsAt)
            return CoverageReplacementExceptionErrors.Invalid;

        var outgoing = source.Shops.SingleOrDefault(s => s.ShopId == command.OutgoingShopId);
        if (outgoing is null) return CoverageReplacementExceptionErrors.Changed;

        var incoming = await db.Database.SqlQuery<IncomingAffiliation>($"""
            SELECT shop_id, shop_name, request_id FROM public.current_shop_affiliations
            WHERE account_id = {account} AND shop_id = {command.IncomingShopId}
            """).SingleOrDefaultAsync(ct);
        if (incoming is null) return CoverageReplacementExceptionErrors.Changed;

        await RetireObsoleteAsync(account, term, command.OutgoingShopId, command.IncomingShopId, now, ct);

        var obs = (await ReadObservedPermitsAsync(account, [outgoing.ShopId], now, ct))
            .GetValueOrDefault((outgoing.ShopId, outgoing.RequestId));
        if (obs?.ValidUntil != command.ObservedPermitDeadline)
            return CoverageReplacementExceptionErrors.Changed;
        if (obs?.ValidUntil is { } permitEnd && command.ProposedPaidCutoverAt < permitEnd)
            return CoverageReplacementExceptionErrors.Invalid;

        var revision = await db.Database.SqlQuery<long>($"""
            SELECT coalesce(max(proposal_revision), 0) + 1 AS "Value"
            FROM public.coverage_replacement_exceptions
            WHERE account_id = {account} AND term_id = {term}
            """).SingleAsync(ct);

        var id = Guid.NewGuid();
        var expires = command.ProposedPaidCutoverAt < now.AddDays(7) ? command.ProposedPaidCutoverAt : now.AddDays(7);
        if (command.ObservedNormalRenewalAt < expires) expires = command.ObservedNormalRenewalAt;
        if (expires <= now) return CoverageReplacementExceptionErrors.Changed;

        var termsDigest = CoverageReplacementExceptionsV1.TermsDigest(account, term, revision,
            outgoing.ShopId, outgoing.RequestId, incoming.ShopId, incoming.RequestId,
            source.Capacity, command.ObservedMemberAllowance, command.RequestedEarlyStartAt,
            command.ProposedPaidCutoverAt, source.EndsAt, command.OverlapDisposition, command.ObservedPermitDeadline);

        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_replacement_exceptions (
                exception_id, account_id, term_id, order_id, proposal_revision,
                outgoing_shop_id, outgoing_shop_name, outgoing_affiliation_id,
                incoming_shop_id, incoming_shop_name, incoming_affiliation_id,
                capacity, member_allowance, normal_renewal_at, requested_early_start_at, paid_cutover_at,
                overlap_disposition, observed_permit_deadline, observed_at, last_contact_at, last_synchronized_at,
                observed_permit_count, request_reason, created_at, expires_at, requester_id, requester_revision,
                operation_key, request_hash, source_hash, terms_digest, state)
            SELECT {id}, {account}, {term}, (SELECT f.order_id FROM public.payment_fulfilments f
                    WHERE f.account_id = {account} AND f.term_id = {term} LIMIT 1), {revision},
                {outgoing.ShopId}, {outgoing.Name}, {outgoing.RequestId},
                a.shop_id, a.shop_name, a.request_id,
                {source.Capacity}, {command.ObservedMemberAllowance}, {source.EndsAt},
                {command.RequestedEarlyStartAt}, {command.ProposedPaidCutoverAt}, {command.OverlapDisposition},
                {command.ObservedPermitDeadline}::timestamptz, {now}, {obs?.LastContactAt}::timestamptz,
                {obs?.LastSynchronizedAt}::timestamptz, {obs?.OutstandingCount ?? 0}, {reason},
                {now}, {expires}, {actor}, m.revision, {command.OperationKey}, {hash}, {source.Hash()}, {termsDigest},
                'AwaitingConsents'
            FROM public.current_shop_affiliations a
            JOIN public.billing_account_members m USING (account_id)
            WHERE a.account_id = {account} AND a.shop_id = {incoming.ShopId} AND a.request_id = {incoming.RequestId}
                AND m.merchant_user_id = {actor} AND m.status = 1 AND m.role = 1
                AND public.slot_shop_eligible({account}, a.shop_id, {now}, public.term_effective_access_end({account}, {term}))
                AND public.replacement_term_available({account}, {term})
                AND NOT EXISTS (SELECT FROM public.active_operational_restrictions r
                    WHERE r.account_id = {account} AND r.effective_at <= {now}
                        AND (r.shop_id IS NULL OR r.shop_id IN ({outgoing.ShopId}, a.shop_id)))
                AND ({command.OverlapDisposition} <> 'CourtesyBridge' OR NOT EXISTS (
                    SELECT FROM public.complimentary_grant_shops g
                    WHERE g.shop_id = a.shop_id AND g.starts_at < {command.ProposedPaidCutoverAt}
                        AND {command.RequestedEarlyStartAt} < g.ends_at))
                AND public.replacement_permit_end({account}, {outgoing.ShopId}, {outgoing.RequestId}, {now})
                    IS NOT DISTINCT FROM {command.ObservedPermitDeadline}::timestamptz
            ON CONFLICT DO NOTHING
            """, ct);
        return inserted == 1
            ? await CustomerRows(account, now).SingleAsync(r => r.ExceptionId == id, ct)
            : CoverageReplacementExceptionErrors.Changed;
    }

    public async Task<ErrorOr<CoverageReplacementExceptionV1>> RecoverCustomerAsync(Guid account, Guid term, Guid actor,
        Guid key, CancellationToken ct) =>
        await LookupCustomerOperationAsync(account, actor, key, ct) is { } prior
        && await CustomerRows(account, EntitlementSnapshotsV1.Instant(clock))
            .SingleOrDefaultAsync(r => r.ExceptionId == prior.Id && r.TermId == term, ct) is { } row
            ? row : CoverageReplacementExceptionErrors.NotFound;

    public async Task<ErrorOr<CoverageReplacementExceptionV1>> OwnerReadAsync(Guid account, Guid shop, Guid exceptionId,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.affiliation_shop_id', {shop.ToString("D")}, true),
                set_config('app.coverage_exception_id', {exceptionId.ToString("D")}, true)
            """, ct);
        var now = EntitlementSnapshotsV1.Instant(clock);
        var row = await CustomerRows(account, now)
            .SingleOrDefaultAsync(r => r.ExceptionId == exceptionId && (r.OutgoingShopId == shop || r.IncomingShopId == shop), ct);
        return row is not null ? row : CoverageReplacementExceptionErrors.NotFound;
    }

    private async Task RetireObsoleteAsync(Guid account, Guid term, Guid outgoing, Guid incoming, DateTimeOffset now,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.exception_evaluated_at', {now}::text, true)", ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacement_exceptions e SET state = 'Blocked', reason = 'SourceChanged'
            WHERE e.account_id = {account}
                AND (e.incoming_shop_id = {incoming} OR (e.term_id = {term} AND e.outgoing_shop_id = {outgoing}))
                AND e.state IN ('AwaitingConsents', 'ReadyForReview')
                AND public.coverage_exception_stale(e.exception_id, {now})
            """, ct);
    }

    private Task<Operation?> LookupCustomerOperationAsync(Guid account, Guid actor, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<Operation>($"""
            SELECT exception_id AS id, request_hash AS hash FROM public.coverage_replacement_exceptions
            WHERE account_id = {account} AND requester_id = {actor} AND operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    private Task<Dictionary<(Guid ShopId, Guid AffiliationRequestId), PermitObservation>> ReadObservedPermitsAsync(
        Guid account, Guid[] shops, DateTimeOffset now, CancellationToken ct) =>
        shops.Length == 0
            ? Task.FromResult(new Dictionary<(Guid, Guid), PermitObservation>())
            : db.Database.SqlQuery<PermitObservation>($"""
                SELECT shop_id, affiliation_request_id,
                    max(valid_until) FILTER (WHERE valid_until > {now}) AS valid_until,
                    max(observed_at) AS last_contact_at,
                    max(observed_at) FILTER (WHERE observation = 'OfflineSync') AS last_synchronized_at,
                    (count(*) FILTER (WHERE valid_until > {now}))::int AS outstanding_count
                FROM public.offline_device_observations
                WHERE account_id = {account} AND shop_id = ANY({shops})
                GROUP BY shop_id, affiliation_request_id
                """).ToDictionaryAsync(o => (o.ShopId, o.AffiliationRequestId), ct);

    private IQueryable<CoverageReplacementExceptionV1> CustomerRows(Guid account, DateTimeOffset now) =>
        db.Database.SqlQuery<CoverageReplacementExceptionV1>($"""
            SELECT e.exception_id, e.account_id, e.term_id, e.proposal_revision, e.terms_digest,
                e.outgoing_shop_id, e.outgoing_shop_name, e.outgoing_affiliation_id,
                e.incoming_shop_id, e.incoming_shop_name, e.incoming_affiliation_id,
                e.capacity, e.member_allowance, e.normal_renewal_at, e.requested_early_start_at, e.paid_cutover_at,
                e.overlap_disposition, e.observed_permit_deadline, e.observed_at,
                e.last_contact_at, e.last_synchronized_at, e.observed_permit_count,
                true AS disconnected_queues_unknown, e.request_reason, e.created_at, e.expires_at,
                CASE WHEN public.coverage_exception_stale(e.exception_id, {now}) THEN 'Blocked' ELSE e.state END AS state,
                e.reason,
                CASE WHEN e.outgoing_revoke_event IS NOT NULL THEN 'Revoked'
                    WHEN e.outgoing_consent_event IS NOT NULL THEN 'Accepted' ELSE 'AwaitingConsent' END AS outgoing_consent_state,
                CASE WHEN e.incoming_revoke_event IS NOT NULL THEN 'Revoked'
                    WHEN e.incoming_consent_event IS NOT NULL THEN 'Accepted' ELSE 'AwaitingConsent' END AS incoming_consent_state,
                h.courtesy_grant_id, h.courtesy_starts_at,
                CASE WHEN h.courtesy_grant_id IS NOT NULL THEN h.cutover_at END AS courtesy_ends_at,
                public.coverage_exception_leg_activation(e.exception_id, 'Outgoing', {now}) AS outgoing_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingPaid', {now}) AS incoming_activation,
                public.coverage_exception_leg_activation(e.exception_id, 'IncomingCourtesy', {now}) AS courtesy_activation,
                e.approved_at, e.applied_at
            FROM public.coverage_replacement_exceptions e
            LEFT JOIN public.paid_slot_handoffs h ON h.exception_id = e.exception_id AND h.account_id = e.account_id
            WHERE e.account_id = {account}
            """);

    private sealed record Operation(Guid Id, string Hash);
    private sealed record IncomingAffiliation(Guid ShopId, string ShopName, Guid RequestId);
    private sealed record PermitObservation(Guid ShopId, Guid AffiliationRequestId, DateTimeOffset? ValidUntil,
        DateTimeOffset? LastContactAt, DateTimeOffset? LastSynchronizedAt, int OutstandingCount);
}
