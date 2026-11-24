using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Changes;

public sealed record ReductionPreviewCreation(CapacityReductionPreviewV1 Preview, bool Created);
public sealed record ReductionScheduleCreation(ScheduledCapacityReductionV1 Schedule, bool Created);

/// <summary>
/// Owner-selected renewal capacity reduction workflow: previews with live merchant counts, explicit schedule
/// confirmation, P03 checkout conversion and drift resolution against the original funded order.
/// </summary>
public sealed partial class ScheduleCapacityReductionHandler(
    IBillingDelegationAccess access, IScheduledCapacityReductionStore store, ICapacityChangeQuoteStore quotes,
    ICheckoutOrderStore orders, IPublishedCatalogueReader catalogue, ICataloguePublicationApproval approval,
    IMerchantReductionObserver observer, TimeProvider clock)
{
    public Task<ErrorOr<ReductionPreviewCreation>> PreviewAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        CreateCapacityReductionPreviewV1 request, CancellationToken ct) =>
        access.ExecuteAsync<ReductionPreviewCreation>(proof, async (_, token) =>
        {
            if (!request.IsValid()) return CapacityReductionErrors.Invalid;
            var hash = CapacityReductionsV1.PreviewCreateHash(term, request);
            if (await store.FindPreviewByOperationAsync(account, request.OperationKey, token) is { } prior)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash && prior.Preview.TermId == term
                    ? new ReductionPreviewCreation(prior.Preview, false) : BillingAccountErrors.PayloadMismatch;

            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            var source = await quotes.SourceAsync(account, term, now, token);
            if (ScheduledChangeValidator.ValidateSource(source, now, request.ObservedCapacity,
                request.ObservedMemberAllowance, request.ObservedPaidThrough) is { } sourceError)
                return sourceError;
            var offer = await TargetOfferAsync(proof, request.TargetPlanVersionId, token);
            var target = ScheduledChangeValidator.ValidateTarget(source!, offer, request.TargetCapacity);
            if (target.IsError) return target.Errors;

            var observed = await observer.ObserveAsync(account, term, source!.Shops, token);
            if (observed.IsError) return observed.Errors;
            if (!MatchesSourceAffiliations(source, observed.Value)) return CapacityReductionErrors.SourceChanged;

            var preview = BuildPreview(account, source, target.Value, observed.Value, now);
            await store.SavePreviewAsync(preview, source, observed.Value, proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return new ReductionPreviewCreation(preview, true);
        }, ct);

    public Task<ErrorOr<CapacityReductionShopPageV1>> PreviewShopsAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, Guid preview, Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<CapacityReductionShopPageV1>(proof, async (_, token) =>
            await store.PreviewShopsPageAsync(account, term, preview, after, token) is { } page
                ? page : CapacityReductionErrors.NotFound, ct);

    public Task<ErrorOr<ReductionScheduleCreation>> ScheduleAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        ScheduleCapacityReductionV1 request, CancellationToken ct) =>
        access.ExecuteAsync<ReductionScheduleCreation>(proof, async (_, token) =>
        {
            if (!request.IsValid()) return CapacityReductionErrors.Invalid;
            var hash = CapacityReductionsV1.ScheduleHash(term, request);
            if (await store.FindScheduleByOperationAsync(account, request.OperationKey, token) is { } prior)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash && prior.Schedule.TermId == term
                    ? new ReductionScheduleCreation(prior.Schedule, false) : BillingAccountErrors.PayloadMismatch;

            if (await store.FindPreviewAsync(account, term, request.PreviewId, token) is not { } preview)
                return CapacityReductionErrors.NotFound;
            if (preview.SnapshotHash != request.PreviewSnapshotHash ||
                preview.CustomerTermsVersion != request.CustomerTermsVersion ||
                preview.RefundPolicyVersion != request.RefundPolicyVersion)
                return CapacityReductionErrors.SourceChanged;

            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            if (now < preview.CreatedAt) return CapacityReductionErrors.SourceChanged;
            if (now >= preview.ExpiresAt) return CapacityReductionErrors.Expired;

            var source = await quotes.SourceAsync(account, term, now, token);
            if (ScheduledChangeValidator.ValidateSource(source, now) is { } sourceError) return sourceError;
            if (await store.FindActiveScheduleAsync(account, term, token) is not null)
                return CapacityReductionErrors.AlreadyScheduled;

            var previewShops = await store.PreviewShopsMapAsync(account, preview.PreviewId, token);
            var live = await observer.ObserveAsync(account, term, source!.Shops, token);
            if (live.IsError) return live.Errors;
            if (ScheduledChangeValidator.ValidateSelection(preview, source, previewShops, live.Value, request.RetainedShopIds) is { } selectionError)
                return selectionError;

            var checkoutSource = await orders.SourceAsync(account, term, [], token);
            var expectedRev = checkoutSource?.Prior?.CapacityRevision ?? 0;
            var retained = new HashSet<Guid>(request.RetainedShopIds);
            var schedule = BuildSchedule(preview, live.Value, request.RetainedShopIds, now);
            var saved = await store.SaveScheduleAsync(schedule, source, live.Value, retained, expectedRev,
                proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return saved ? new ReductionScheduleCreation(schedule, true) : CapacityReductionErrors.AlreadyScheduled;
        }, ct);

    public Task<ErrorOr<ScheduledCapacityReductionV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        CancellationToken ct) =>
        access.ExecuteAsync<ScheduledCapacityReductionV1>(proof, async (_, token) =>
            await store.FindActiveScheduleAsync(account, term, token) is { } active
                ? active.Schedule : CapacityReductionErrors.NotFound, ct);

    public Task<ErrorOr<ScheduledCapacityReductionV1>> RecoverAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, Guid operationKey, CancellationToken ct) =>
        access.ExecuteAsync<ScheduledCapacityReductionV1>(proof, async (_, token) =>
            await store.FindScheduleByOperationAsync(account, operationKey, token) is { } stored &&
            stored.ActorId == proof.ActorId && stored.Schedule.TermId == term
                ? stored.Schedule : CapacityReductionErrors.NotFound, ct);

    public Task<ErrorOr<CapacityReductionShopPageV1>> ShopsAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, Guid schedule, Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<CapacityReductionShopPageV1>(proof, async (_, token) =>
            await store.ScheduleShopsPageAsync(account, term, schedule, after, token) is { } page
                ? page : CapacityReductionErrors.NotFound, ct);

    private async Task<PublishedPlanTermsV1?> TargetOfferAsync(VerifiedBillingDelegation proof, Guid version, CancellationToken ct) =>
        approval.PublicFixtureFilter is { } filter &&
        await catalogue.ReadVersionAsync(proof.ReplayId, version, filter, true, ct) is
            { AvailableForNewPurchase: true, Terms: { Kind: "single-shop" or "multi-shop" } offer }
            ? offer : null;

    private static bool MatchesSourceAffiliations(CapacityChangeSource source,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed) =>
        observed.Count == source.Shops.Length &&
        source.Shops.All(shop => observed.TryGetValue(shop.ShopId, out var seen) &&
            seen.AffiliationMatched && seen.AffiliationRequestId == shop.RequestId);

    private static CapacityReductionPreviewV1 BuildPreview(Guid account, CapacityChangeSource source,
        ReductionTargetTerms target, IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, DateTimeOffset now)
    {
        var offer = target.Offer;
        var conflicted = source.Shops.Count(s => observed[s.ShopId].ActiveMembers > offer.MembersPerShop);
        var digest = ReductionTransportV1.ObservationDigest(source.Shops.Select(s =>
            (s.ShopId, s.RequestId, observed[s.ShopId].ActiveMembers, observed[s.ShopId].SeatHash)));
        var preview = new CapacityReductionPreviewV1(account, Guid.NewGuid(), source.TermId, source.PlanVersionId,
            offer.PlanId, offer.VersionId, offer.VersionNumber, offer.Name, target.Period, source.Capacity,
            target.TargetCapacity, source.MemberAllowance!.Value, offer.MembersPerShop, target.TargetMode,
            source.Capacity - target.TargetCapacity, Math.Max(0, source.Capacity - source.Shops.Length),
            Math.Max(0, source.Shops.Length - target.TargetCapacity), source.Shops.Length,
            CheckoutOrdersV1.ShopHash(source.Shops.Select(s => s.ShopId)), source.Hash(), digest,
            conflicted > 0, conflicted, target.AmountPaise, offer.Currency, offer.PriceBasis,
            offer.CustomerTermsVersion, offer.RefundPolicyVersion, BillingCalendar.Zone, BillingCalendar.Version,
            RenewalPolicy.Version, ScheduledChangeValidator.AnchorText(target.Anchor), target.NextOrdinal,
            source.StartsAt, source.EndsAt, target.NextStartsAt, target.NextEndsAt, source.Suspended,
            now, now + CheckoutQuotePolicy.Lifetime, offer.DevelopmentFixture, string.Empty);
        return preview with { SnapshotHash = CapacityReductionsV1.PreviewSnapshotHash(preview) };
    }

    private static ScheduledCapacityReductionV1 BuildSchedule(CapacityReductionPreviewV1 p,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, Guid[] retainedIds, DateTimeOffset now)
    {
        var digest = ReductionTransportV1.ObservationDigest(observed.Values.Select(s =>
            (s.ShopId, s.AffiliationRequestId, s.ActiveMembers, s.SeatHash)));
        var schedule = new ScheduledCapacityReductionV1(p.AccountId, Guid.NewGuid(), p.PreviewId, p.TermId,
            "Scheduled", null, p.TargetPlanId, p.TargetPlanVersionId, p.TargetPlanVersion, p.TargetPlanName,
            p.Period, p.PriorCapacity, p.TargetCapacity, p.PriorMemberAllowance, p.TargetMemberAllowance,
            p.TargetMode, p.SourceShopCount, p.SourceShopHash, retainedIds.Length, CheckoutOrdersV1.ShopHash(retainedIds),
            p.SourceHash, digest, p.AmountPaise, p.Currency, p.PriceBasis, p.CustomerTermsVersion, p.RefundPolicyVersion,
            p.BillingTimeZone, p.CalendarVersion, p.RenewalPolicyVersion, p.AnchorLocal, p.Ordinal, p.CurrentStartsAt,
            p.CurrentPaidThrough, p.RenewalStartsAt, p.RenewalEndsAt, 0, null, null, p.SuspensionDisclosed,
            p.DevelopmentFixture, now, null, null, string.Empty);
        return schedule with { SnapshotHash = CapacityReductionsV1.ScheduleSnapshotHash(schedule) };
    }
}
