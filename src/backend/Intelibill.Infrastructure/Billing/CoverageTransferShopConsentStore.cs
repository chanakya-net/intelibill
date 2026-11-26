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
using static Intelibill.Shared.Contracts.V1.CoverageTransfersV1;
using Owner = Intelibill.Infrastructure.Billing.ShopAffiliationConsentStore.LiveOwner;

namespace Intelibill.Infrastructure.Billing;

/// <summary>
/// The named shop owner's accept or revoke of a coverage-transfer proposal. Decided under the shop licence fence and
/// the user → shop → membership locks against the live verified owner, then appended with its outbox message in one
/// transaction. It records evidence only: no licence state, affiliation or membership is written.
/// </summary>
internal sealed class CoverageTransferShopConsentStore(
    ApplicationDbContext db,
    ShopAffiliationConsentStore authority,
    ShopLicenceFence fence,
    TimeProvider transportClock,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
{
    private static Error Changed => Error.Conflict("Coverage.TransferChanged");
    private static Error ConsentInvalid => Error.Conflict("Coverage.TransferConsentInvalid");
    private static Error AuthorityChanged => Error.Conflict("Coverage.TransferAuthorityChanged");

    internal async Task<ErrorOr<CoverageTransferShopOwnerV1>> RunAsync(MerchantBillingCaller caller,
        CoverageTransferOwnerTermsV1 terms, CoverageTransferShopAction action, CoverageTransferDecisionV1? decision,
        CancellationToken ct)
    {
        var t = terms.Transfer;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await fence.EnterProjectionAsync(t.SourceAccountId, t.ShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (await authority.LockOwnerAsync(caller, t.ShopId, ct) is not { } owner)
            return ShopAffiliationConsentErrors.OwnerRequired;

        var events = await EventsAsync(t, owner, decision?.OperationKey ?? Guid.Empty, ct);
        var own = events.Where(e => e.TransferId == t.TransferId).ToArray();
        var status = Status(own, owner);
        if (action == CoverageTransferShopAction.Read || decision is null)
            return View(t, own, status, owner);

        var kind = action == CoverageTransferShopAction.Accept ? Accepted : Revoked;
        var hash = SignedServiceAssertion.Digest(JsonSerializer.SerializeToUtf8Bytes(
            new { t.TransferId, kind, decision.ExpectedProposalRevision, decision.ExpectedTermsDigest, decision.ExpectedAuthorityRevision }));
        if (events.SingleOrDefault(e => e.ActorId == owner.ActorId && e.OperationKey == decision.OperationKey) is { } replay)
            return replay.TransferId != t.TransferId || replay.RequestHash != hash ? MerchantBillingErrors.PayloadMismatch
                : replay.Matches(owner) ? View(t, own, status, owner) : AuthorityChanged;

        if (decision.ExpectedProposalRevision != t.ProposalRevision || decision.ExpectedTermsDigest != t.TermsDigest ||
            decision.ExpectedAuthorityRevision != owner.Version)
            return Changed;
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var latest = own.Length == 0 ? 0 : own.Max(e => e.Generation);
        int? generation = kind switch
        {
            Accepted when t.State is AwaitingConsents or ConsentsCollected && now < t.ExpiresAt
                && status is AwaitingConsent or Revoked or Stale => latest + 1,
            Revoked when status is Accepted or Stale => latest,
            _ => null,
        };
        if (generation is null || !await BoundAsync(t, terms.SourceAffiliationId, ct)) return ConsentInvalid;

        await SaveAsync(terms, owner, kind, generation.Value, decision.OperationKey, hash, now, ct);
        await tx.CommitAsync(ct);
        return View(t, [.. own, new(Guid.Empty, t.TransferId, generation.Value, kind, owner.ActorId, owner.MembershipId,
            owner.Version, decision.OperationKey, hash)], kind, owner);
    }

    // The latest generation decides; a revocation ends it. An acceptance stands only for the exact live owner epoch.
    private static string Status(ConsentEvent[] events, Owner owner)
    {
        if (events.Length == 0) return AwaitingConsent;
        var latest = events.Where(e => e.Generation == events.Max(x => x.Generation)).ToArray();
        return latest.Any(e => e.Kind == Revoked) ? Revoked : latest.Single().Matches(owner) ? Accepted : Stale;
    }

    private static CoverageTransferShopOwnerV1 View(CoverageTransferV1 t, ConsentEvent[] own, string status, Owner owner)
    {
        var recorded = t.Consents.Single(c => c.Purpose == ShopOwner);
        var latest = own.Length == 0 ? 0 : own.Max(e => e.Generation);
        var revoked = own.Any(e => e.Generation == latest && e.Kind == Revoked);
        var delivery = latest == 0 ? "None"
            : recorded.Generation == latest && (recorded.Status == Revoked) == revoked ? "Recorded" : "Pending";
        return new(t.TransferId, t.ProposalRevision, t.TermsDigest, t.SourceAccountId, t.SourceAccountName,
            t.DestinationAccountId, t.DestinationAccountName, t.ShopId, t.ShopName, t.Treatment, t.CutoverAt,
            t.SourcePaidThroughAt, t.ObservedPermitCount, t.ObservedPermitDeadline, t.DisconnectedQueuesUnknown,
            t.ExpiresAt, t.State, status, delivery, owner.Version);
    }

    private Task<ConsentEvent[]> EventsAsync(CoverageTransferV1 t, Owner owner, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<ConsentEvent>($"""
            SELECT id, transfer_id, generation, kind, actor_id, membership_id, authority_version, operation_key, request_hash
            FROM public.coverage_transfer_consent_events
            WHERE shop_id = {t.ShopId} AND (transfer_id = {t.TransferId} OR (actor_id = {owner.ActorId} AND operation_key = {key}))
            """).ToArrayAsync(ct);

    // The shop must still be bound to the source account by the exact affiliation the proposal froze.
    private Task<bool> BoundAsync(CoverageTransferV1 t, Guid affiliation, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.shop_affiliation_consent_events a
                LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = a.id
                LEFT JOIN public.shop_license_states s ON s.shop_id = a.shop_id
                WHERE a.shop_id = {t.ShopId} AND a.account_id = {t.SourceAccountId} AND a.request_id = {affiliation}
                    AND a.kind = 'Accepted' AND r.status IS DISTINCT FROM 'NeedsReview'
                    AND (s.shop_id IS NULL OR (s.account_id = {t.SourceAccountId}
                        AND s.affiliation_request_id = {affiliation} AND s.state <> 'Closed'))) AS "Value"
            """).SingleAsync(ct);

    private async Task SaveAsync(CoverageTransferOwnerTermsV1 terms, Owner owner, string kind, int generation, Guid key,
        string hash, DateTimeOffset now, CancellationToken ct)
    {
        var t = terms.Transfer;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_transfer_consent_events (id, shop_id, account_id, destination_account_id, transfer_id,
                proposal_revision, terms_digest, affiliation_request_id, actor_id, membership_id, authority_version,
                operation_key, request_hash, generation, kind, recorded_at)
            VALUES ({id}, {t.ShopId}, {t.SourceAccountId}, {t.DestinationAccountId}, {t.TransferId}, {t.ProposalRevision},
                {t.TermsDigest}, {terms.SourceAffiliationId}, {owner.ActorId}, {owner.MembershipId}, {owner.Version},
                {key}, {hash}, {generation}, {kind}, {now})
            """, ct);
        var receipt = new CoverageTransferShopConsentV1(SchemaVersion, id, t.TransferId, t.SourceAccountId,
            t.DestinationAccountId, t.ShopId, terms.SourceAffiliationId, t.ProposalRevision, t.TermsDigest,
            owner.ActorId, owner.MembershipId, owner.Version, generation, kind, now);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(id, t.SourceAccountId, t.ShopId, ConsentKind, bytes,
            SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(transportClock), ct);
    }

    private sealed record ConsentEvent(Guid Id, Guid TransferId, int Generation, string Kind, Guid ActorId,
        Guid MembershipId, long AuthorityVersion, Guid OperationKey, string RequestHash)
    {
        internal bool Matches(Owner owner) =>
            ActorId == owner.ActorId && MembershipId == owner.MembershipId && AuthorityVersion == owner.Version;
    }
}
