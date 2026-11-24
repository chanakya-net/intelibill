using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

public sealed class ConvertCapacityQuoteHandler(IBillingDelegationAccess access, ICapacityChangeQuoteStore changes,
    ICheckoutOrderStore orders, IPublishedCatalogueReader catalogue, ICataloguePublicationApproval approval, TimeProvider clock)
{
    public Task<ErrorOr<CheckoutQuoteV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid term, Guid id,
        ConvertCapacityQuoteV1 request, CancellationToken ct) =>
        access.ExecuteAsync<CheckoutQuoteV1>(proof, async (_, token) =>
        {
            if (request.OperationKey == Guid.Empty || !CheckoutOrdersV1.IsHash(request.SnapshotHash))
                return CheckoutOrderErrors.Invalid;
            // Reading the sealed quote requires a live billing owner under RLS, including on replay.
            if (await changes.PayableAsync(account, term, id, proof.ActorId, token) is not { } sealedQuote)
                return CheckoutOrderErrors.Missing;
            var (q, pin, revision) = sealedQuote;
            if (q.SnapshotHash != request.SnapshotHash)
                return CheckoutOrderErrors.Changed;
            var hash = CheckoutOrdersV1.Digest(CapacityIncreaseOrderV1.Rule, term, id, q.SnapshotHash);
            if (await orders.FindAsync(account, request.OperationKey, true, token) is { } reused)
                return reused.ActorId == proof.ActorId && reused.RequestHash == hash ? reused.Quote : BillingAccountErrors.PayloadMismatch;
            // One order per reviewed quote even across tabs with different operation keys.
            if (await orders.FindAsync(account, id, false, token) is { } prior)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash ? prior.Quote : BillingAccountErrors.PayloadMismatch;
            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            if (now < q.CreatedAt || now >= q.ValidUntil)
                return CheckoutOrderErrors.Expired;
            var live = await changes.SourceAsync(account, term, now, token);
            if (live is not { CompleteEvidence: true, Closed: false, Superseded: false } || live.Hash() != q.SourceHash ||
                live.Suspended != q.SuspensionDisclosed || await orders.SourceAsync(account, term, [], token) is not { } source ||
                approval.PublicFixtureFilter is not { } filter || await catalogue.ReadVersionAsync(proof.ReplayId,
                    q.TargetPlanVersionId, filter, true, token) is not { AvailableForNewPurchase: true, Terms: { } offer })
                return CheckoutOrderErrors.Changed;
            var order = Build(q, source, offer, pin, revision);
            await orders.SaveAsync(order, source, proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return order;
        }, ct);

    internal static string PinHash(PaymentProviderPin pin) =>
        CheckoutOrdersV1.Digest(pin.ProviderId, pin.ProviderAccountId, pin.Environment, pin.ConfigurationVersion);

    private static CheckoutQuoteV1 Build(CapacityChangeQuoteV1 q, CheckoutSource source, PublishedPlanTermsV1 offer,
        PaymentProviderPin pin, long revision)
    {
        var order = new CheckoutQuoteV1(q.AccountId, q.QuoteId, 1, q.TargetPlanId, q.TargetPlanVersionId, q.TargetPlanVersion,
            q.TargetPlanName, q.Period, q.TermId, q.TargetCapacity, q.TargetMemberAllowance, q.ShopCount, q.ShopHash,
            source.Hash(), q.AmountPaise!.Value, q.Currency, q.PriceBasis, offer.CustomerTermsVersion, offer.RefundPolicyVersion,
            q.BillingTimeZone, q.CalendarVersion, RenewalPolicy.Version, CapacityIncreaseOrderV1.Rule, q.IntervalStartsAt,
            q.PaidThrough, 0, q.AnchorLocal!, q.Ordinal!.Value, q.CreatedAt, q.ExpiresAt, q.DevelopmentFixture, "",
            Increase: new(q.SnapshotHash, PinHash(pin), revision, source.Prior!.CapacityRevision));
        return order with { SnapshotHash = CheckoutOrdersV1.SnapshotHash(order) };
    }
}
