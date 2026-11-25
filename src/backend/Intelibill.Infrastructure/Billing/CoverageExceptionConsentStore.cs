using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Owner = Intelibill.Infrastructure.Billing.ShopAffiliationConsentStore.LiveOwner;

namespace Intelibill.Infrastructure.Billing;

internal sealed class CoverageExceptionConsentStore(
    ApplicationDbContext db,
    ShopAffiliationConsentStore authority,
    ShopLicenceFence fence,
    TimeProvider transportClock,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
{
    internal async Task<ErrorOr<CoverageReplacementExceptionOwnerV1>> RunAsync(
        MerchantBillingCaller caller, CoverageReplacementExceptionV1 row, Guid shopId,
        ShopAffiliationConsentAction action, Guid key, CancellationToken ct)
    {
        var leg = shopId == row.OutgoingShopId ? CoverageReplacementExceptionsV1.OutgoingLeg
            : shopId == row.IncomingShopId ? CoverageReplacementExceptionsV1.IncomingLeg : null;
        if (leg is null) return ShopAffiliationConsentErrors.OwnerRequired;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await fence.EnterProjectionAsync(row.AccountId, shopId, ct);
        if (entered.IsError) return entered.Errors;
        if (await authority.LockOwnerAsync(caller, shopId, ct) is not { } owner)
            return ShopAffiliationConsentErrors.OwnerRequired;

        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var events = await db.Database.SqlQuery<ConsentEvent>($"""
            SELECT exception_id, leg, proposal_revision, terms_digest, actor_id, membership_id,
                authority_version, kind, operation_key
            FROM public.coverage_exception_consent_events
            WHERE shop_id = {shopId} AND (exception_id = {row.ExceptionId} OR (actor_id = {owner.ActorId} AND operation_key = {key}))
            """).ToArrayAsync(ct);

        var prior = events.Where(e => e.ExceptionId == row.ExceptionId && e.Leg == leg).ToArray();
        var status = Status(prior, owner, row, now);
        if (action == ShopAffiliationConsentAction.Read)
            return View(row, leg, shopId, status);

        var kind = action switch
        {
            ShopAffiliationConsentAction.Grant => "Granted",
            ShopAffiliationConsentAction.Revoke => "Revoked",
            ShopAffiliationConsentAction.Accept => "Accepted",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        if (events.SingleOrDefault(e => e.ActorId == owner.ActorId && e.OperationKey == key) is { } replay)
            return replay.ExceptionId == row.ExceptionId && replay.Leg == leg && replay.Kind == kind
                && replay.ProposalRevision == row.ProposalRevision && replay.TermsDigest == row.TermsDigest
                && replay.Matches(owner)
                ? View(row, leg, shopId, status)
                : MerchantBillingErrors.PayloadMismatch;

        var consumed = await ConsumedAsync(row.ExceptionId, ct);
        var allowed = kind switch
        {
            "Granted" => row.State is "AwaitingConsents" or "ReadyForReview" && status == "AwaitingConsent" && !consumed,
            "Accepted" => row.State is "AwaitingConsents" or "ReadyForReview" && status == "Granted" && !consumed,
            _ => row.State is "AwaitingConsents" or "ReadyForReview" or "Preparing" && status is "Granted" or "Accepted" && !consumed,
        };
        if (!allowed || now < row.CreatedAt || (kind != "Revoked" && now >= row.ExpiresAt))
            return ShopAffiliationConsentErrors.ConsentInvalid;

        var affiliationId = leg == CoverageReplacementExceptionsV1.OutgoingLeg
            ? row.OutgoingAffiliationId : row.IncomingAffiliationId;
        var seats = await ReadSeatsAsync(row, shopId, affiliationId, leg, now, kind == "Revoked", ct);
        if (seats is null) return ShopAffiliationConsentErrors.ConsentInvalid;
        if (kind == "Accepted" && leg == CoverageReplacementExceptionsV1.IncomingLeg && seats.ActiveMembers > row.MemberAllowance)
            return Error.Conflict("Coverage.MemberCapacityExceeded");

        await SaveAsync(row, leg, shopId, affiliationId, owner, kind, key, now, seats, ct);
        await tx.CommitAsync(ct);
        return View(row, leg, shopId, kind);
    }

    private static CoverageReplacementExceptionOwnerV1 View(CoverageReplacementExceptionV1 row, string leg,
        Guid shopId, string consentStatus)
    {
        var isOutgoing = leg == CoverageReplacementExceptionsV1.OutgoingLeg;
        var shopName = isOutgoing ? row.OutgoingShopName : row.IncomingShopName;
        var counterparty = isOutgoing ? row.IncomingShopName : row.OutgoingShopName;
        var activation = isOutgoing ? row.OutgoingActivation
            : row.OverlapDisposition == CoverageReplacementExceptionsV1.CourtesyBridgeDisposition
              && row.IncomingActivation is "Pending" or "Waiting"
                ? row.CourtesyActivation : row.IncomingActivation;
        return new(row.ExceptionId, row.AccountId, row.TermId, row.ProposalRevision, row.TermsDigest,
            leg, shopId, shopName, counterparty, row.NormalRenewalAt, row.RequestedEarlyStartAt,
            row.PaidCutoverAt, row.OverlapDisposition, row.MemberAllowance, row.ObservedPermitDeadline,
            row.DisconnectedQueuesUnknown, row.State, consentStatus, activation, row.ExpiresAt);
    }

    private Task<bool> ConsumedAsync(Guid exceptionId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.merchant_coverage_exception_decisions
                WHERE exception_id = {exceptionId} AND outcome = 'Consumed') AS "Value"
            """).SingleAsync(ct);

    private Task<Seats?> ReadSeatsAsync(CoverageReplacementExceptionV1 row, Guid shopId, Guid affiliationId,
        string leg, DateTimeOffset now, bool revoking, CancellationToken ct) =>
        db.Database.SqlQuery<Seats>($"""
            SELECT (SELECT count(*)::int FROM public.shop_memberships WHERE shop_id = {shopId} AND status = 'Active') AS active_members,
                (SELECT encode(sha256(convert_to(coalesce(string_agg(id::text || '/' || authority_version::text, ',' ORDER BY id), ''), 'UTF8')), 'hex')
                    FROM public.shop_memberships WHERE shop_id = {shopId}) AS seat_hash
            FROM public.shop_affiliation_consent_events a
            LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = a.id
            LEFT JOIN public.shop_license_states s ON s.shop_id = a.shop_id
            WHERE a.shop_id = {shopId} AND a.account_id = {row.AccountId} AND a.request_id = {affiliationId}
                AND a.kind = 'Accepted' AND r.status IS DISTINCT FROM 'NeedsReview'
                AND ({revoking} OR (
                    (s.shop_id IS NULL OR (s.account_id = {row.AccountId} AND s.affiliation_request_id = {affiliationId} AND s.state <> 'Closed'))
                    AND ({leg} = 'Outgoing' OR s.shop_id IS NULL OR s.term_id IS NULL OR s.access_ends_at <= {now})))
            """).SingleOrDefaultAsync(ct);

    private async Task SaveAsync(CoverageReplacementExceptionV1 row, string leg, Guid shopId, Guid affiliationId,
        Owner owner, string kind, Guid key, DateTimeOffset now, Seats seats, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_exception_consent_events (
                id, shop_id, account_id, exception_id, proposal_revision, terms_digest, leg,
                affiliation_request_id, term_id, actor_id, membership_id, authority_version,
                operation_key, kind, recorded_at, expires_at, requested_early_start_at,
                paid_cutover_at, normal_renewal_at, overlap_disposition, member_allowance,
                active_members, seat_hash)
            VALUES ({id}, {shopId}, {row.AccountId}, {row.ExceptionId}, {row.ProposalRevision}, {row.TermsDigest}, {leg},
                {affiliationId}, {row.TermId}, {owner.ActorId}, {owner.MembershipId}, {owner.Version},
                {key}, {kind}, {now}, {row.ExpiresAt}, {row.RequestedEarlyStartAt},
                {row.PaidCutoverAt}, {row.NormalRenewalAt}, {row.OverlapDisposition}, {row.MemberAllowance},
                {(kind == "Accepted" ? (int?)seats.ActiveMembers : null)}::int,
                {(kind == "Accepted" ? seats.SeatHash : null)}::text)
            """, ct);
        if (kind == "Granted") return;

        var receipt = new CoverageReplacementExceptionConsentV1(
            CoverageReplacementExceptionsV1.SchemaVersion, id, row.ExceptionId, row.AccountId, row.TermId,
            row.ProposalRevision, row.TermsDigest, leg, shopId, affiliationId,
            owner.ActorId, owner.MembershipId, owner.Version, seats.ActiveMembers, seats.SeatHash,
            row.MemberAllowance, row.RequestedEarlyStartAt, row.PaidCutoverAt, row.NormalRenewalAt,
            row.OverlapDisposition, now, kind);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(id, row.AccountId, shopId, CoverageReplacementExceptionsV1.ConsentKind,
            bytes, SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(transportClock), ct);
    }

    private static string Status(ConsentEvent[] events, Owner owner, CoverageReplacementExceptionV1 row, DateTimeOffset now)
    {
        if (events.Any(e => e.Kind == "Revoked")) return "Revoked";
        if (events.Any(e => e.Kind == "Accepted")) return "Accepted";
        if (row.State is "Blocked" or "Refused") return row.State;
        if (now >= row.ExpiresAt) return "Expired";
        var grant = events.SingleOrDefault(e => e.Kind == "Granted");
        return grant is null ? "AwaitingConsent"
            : grant.Matches(owner) && grant.ProposalRevision == row.ProposalRevision && grant.TermsDigest == row.TermsDigest
                ? "Granted" : "Stale";
    }

    private sealed record Seats(int ActiveMembers, string SeatHash);

    private sealed record ConsentEvent(Guid ExceptionId, string Leg, long ProposalRevision, string TermsDigest,
        Guid ActorId, Guid MembershipId, long AuthorityVersion, string Kind, Guid OperationKey)
    {
        internal bool Matches(Owner owner) =>
            ActorId == owner.ActorId && MembershipId == owner.MembershipId && AuthorityVersion == owner.Version;
    }
}
