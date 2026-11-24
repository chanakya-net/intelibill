using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Changes;

public sealed partial class ScheduleCapacityReductionHandler
{
    public Task<ErrorOr<CheckoutQuoteV1>> CreateOrderAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        Guid scheduleId, CreateReductionOrderV1 request, CancellationToken ct) =>
        access.ExecuteAsync<CheckoutQuoteV1>(proof, async (_, token) =>
        {
            if (!await store.IsLiveOwnerAsync(account, proof.ActorId, token))
                return BillingDelegationErrors.Denied;
            if (!request.IsValid()) return CheckoutOrderErrors.Invalid;
            if (await store.FindScheduleAsync(account, term, scheduleId, token) is not { Schedule: var schedule })
                return CapacityReductionErrors.NotFound;
            if (schedule.SnapshotHash != request.SnapshotHash)
                return CheckoutOrderErrors.Changed;

            var hash = CheckoutOrdersV1.Digest(CapacityReductionsV1.Policy, "order", term, scheduleId, request.SnapshotHash);
            if (await orders.FindAsync(account, request.OperationKey, true, token) is { } reused)
                return reused.ActorId == proof.ActorId && reused.RequestHash == hash
                    ? reused.Quote : BillingAccountErrors.PayloadMismatch;
            if (schedule.State != "Scheduled")
                return CapacityReductionErrors.AlreadyFunded;

            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            if (await store.FindReusableOrderAsync(account, scheduleId, now, token) is { } existingOrderId &&
                await orders.FindAsync(account, existingOrderId, false, token) is { } prior)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash
                    ? prior.Quote : BillingAccountErrors.PayloadMismatch;

            var source = await quotes.SourceAsync(account, term, now, token);
            if (ScheduledChangeValidator.ValidateSource(source, now) is { } sourceError)
                return sourceError == CapacityReductionErrors.SourceChanged ? CheckoutOrderErrors.Changed : sourceError;
            if (source!.Hash() != schedule.SourceHash || source.Suspended != schedule.SuspensionDisclosed)
                return CheckoutOrderErrors.Changed;

            var frozenShops = await store.ScheduleShopsAsync(account, scheduleId, token);
            var live = await observer.ObserveAsync(account, term, source.Shops, token);
            if (live.IsError) return live.Errors;
            var drift = CheckLiveFit(schedule, frozenShops, live.Value);
            if (drift is { } driftError) return driftError;

            var checkoutSource = await orders.SourceAsync(account, term, [], token);
            if (checkoutSource?.Prior is not { } priorTerm) return CheckoutOrderErrors.Changed;
            var retainedSet = frozenShops.Where(s => s.Retained).Select(s => s.ShopId).ToHashSet();
            var retainedCheckoutShops = checkoutSource.Shops.Where(s => retainedSet.Contains(s.ShopId)).ToArray();
            if (retainedCheckoutShops.Length != schedule.RetainedShopCount) return CheckoutOrderErrors.Changed;

            var order = BuildOrder(schedule, checkoutSource, priorTerm, now);
            var orderSource = new CheckoutSource(retainedCheckoutShops, priorTerm);
            await orders.SaveAsync(order, orderSource, proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return order;
        }, ct);

    public Task<ErrorOr<ScheduledCapacityReductionV1>> ResolveAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, Guid scheduleId, ResolveCapacityReductionV1 request, CancellationToken ct) =>
        access.ExecuteAsync<ScheduledCapacityReductionV1>(proof, async (_, token) =>
        {
            if (!request.IsValid()) return CapacityReductionErrors.Invalid;
            var hash = CapacityReductionsV1.ResolveHash(term, scheduleId, request);
            if (await store.FindResolutionByOperationAsync(account, request.OperationKey, token) is { } prior)
            {
                if (prior.ActorId != proof.ActorId || prior.ScheduleId != scheduleId || prior.RequestHash != hash)
                    return BillingAccountErrors.PayloadMismatch;
                return (await store.FindScheduleAsync(account, term, scheduleId, token)) is { Schedule: var replay }
                    ? replay : CapacityReductionErrors.NotFound;
            }

            if (await store.FindScheduleAsync(account, term, scheduleId, token) is not { Schedule: var schedule })
                return CapacityReductionErrors.NotFound;
            if (schedule.SnapshotHash != request.SnapshotHash || schedule.ResolutionGeneration != request.ExpectedGeneration)
                return CapacityReductionErrors.SourceChanged;
            if (schedule.State != "NeedsResolution")
                return CapacityReductionErrors.SourceChanged;

            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            var source = await quotes.SourceAsync(account, term, now, token);
            if (source is not { CompleteEvidence: true, Superseded: false, Closed: false, CancellationRevision: var rev } ||
                rev % 2 != 0 || source.Hash() != schedule.SourceHash || source.Suspended != schedule.SuspensionDisclosed ||
                now >= schedule.RenewalEndsAt)
                return CapacityReductionErrors.SourceChanged;

            var frozenShops = await store.ScheduleShopsAsync(account, scheduleId, token);
            var live = await observer.ObserveAsync(account, term, source.Shops, token);
            if (live.IsError) return live.Errors;
            if (CheckLiveFit(schedule, frozenShops, live.Value) is { } drift) return drift;

            var queued = await store.ResolveAndQueueAsync(schedule, frozenShops, live.Value, proof.ActorId,
                request.OperationKey, hash, proof.ReplayId, now, token);
            if (!queued) return CapacityReductionErrors.SourceChanged;
            return (await store.FindScheduleAsync(account, term, scheduleId, token))!.Schedule;
        }, ct);

    private static Error? CheckLiveFit(ScheduledCapacityReductionV1 schedule, StoredReductionShop[] frozenShops,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> live)
    {
        if (live.Count != frozenShops.Length) return CapacityReductionErrors.SourceChanged;
        foreach (var shop in frozenShops)
        {
            if (!live.TryGetValue(shop.ShopId, out var seen) || !seen.AffiliationMatched ||
                seen.AffiliationRequestId != shop.AffiliationRequestId)
                return CapacityReductionErrors.SourceChanged;
            if (shop.Retained && (seen.ActiveMembers <= 0 || seen.ActiveMembers > schedule.TargetMemberAllowance))
                return CapacityReductionErrors.MemberConflict;
        }
        return null;
    }

    private static CheckoutQuoteV1 BuildOrder(ScheduledCapacityReductionV1 s, CheckoutSource fullSource,
        CheckoutRenewalSource prior, DateTimeOffset now)
    {
        var period = s.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        var anchor = new BillingAnchor(prior.AnchorLocal, BillingCalendar.Zone, period);
        var decision = RenewalPolicy.DetermineTerm(new(period, now, SubscriptionBasis.Paid, prior.EndsAt, anchor,
            prior.Ordinal, AcceptedQuote: true));
        var grace = decision.Start == RenewalStart.PriorPaidThrough ? decision.IncludedGraceDays : 0;
        var quote = new CheckoutQuoteV1(s.AccountId, Guid.NewGuid(), 1, s.TargetPlanId, s.TargetPlanVersionId,
            s.TargetPlanVersion, s.TargetPlanName, s.Period, s.TermId, s.TargetCapacity, s.TargetMemberAllowance,
            s.RetainedShopCount, s.RetainedShopHash, fullSource.Hash(), s.AmountPaise, s.Currency, s.PriceBasis,
            s.CustomerTermsVersion, s.RefundPolicyVersion, s.BillingTimeZone, s.CalendarVersion, s.RenewalPolicyVersion,
            CheckoutRenewalPolicy.Renewal, s.EffectiveAt, s.RenewalEndsAt, grace, s.AnchorLocal, s.Ordinal,
            now, now + CheckoutQuotePolicy.Lifetime, s.DevelopmentFixture, string.Empty,
            Reduction: new(s.ScheduleId, s.SnapshotHash, s.SourceShopCount, s.SourceShopHash));
        return quote with { SnapshotHash = CheckoutOrdersV1.SnapshotHash(quote) };
    }
}
