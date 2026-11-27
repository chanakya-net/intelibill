using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

public static class CapacityChangeQuoteErrors
{
    public static Error Invalid => Error.Validation(CapacityChangeQuotesV1.Invalid,
        "Choose a published package that increases capacity without reducing the member allowance.");
    public static Error Missing => Error.NotFound(CapacityChangeQuotesV1.NotFound, "The subscription term or quote was not found.");
    public static Error Changed => Error.Conflict(CapacityChangeQuotesV1.SourceChanged,
        "The subscription term changed. Review its current capacity and paid-through date.");
    public static Error Review => Error.Conflict(CapacityChangeQuotesV1.NeedsReview,
        "This subscription's recorded evidence needs provider review before a change can be quoted.");
}

public sealed record CapacityChangeQuoteCreation(CapacityChangeQuoteV1 Quote, bool Created);

/// <summary>
/// Freezes one capacity-increase preview under the owner's delegation, the account lock and live membership. A retry
/// with the same key, actor, term and payload returns the original quote unchanged, even after it expires.
/// </summary>
public sealed class CreateCapacityChangeQuoteHandler(IBillingDelegationAccess access, ICapacityChangeQuoteStore store,
    IPublishedCatalogueReader catalogue, ICataloguePublicationApproval approval, IPaymentProviderSelection selection,
    IPaymentGatewayResolver gateways, TimeProvider clock)
{
    public Task<ErrorOr<CapacityChangeQuoteCreation>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        CreateCapacityChangeQuoteV1 request, CancellationToken ct) =>
        access.ExecuteAsync<CapacityChangeQuoteCreation>(proof, async (_, token) =>
        {
            if (!request.IsValid())
                return CapacityChangeQuoteErrors.Invalid;
            var hash = CapacityChangeQuotesV1.CreateHash(term, request);
            if (await store.FindAsync(account, request.OperationKey, token) is { } prior)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash && prior.Quote.TermId == term
                    ? new CapacityChangeQuoteCreation(prior.Quote, false) : BillingAccountErrors.PayloadMismatch;

            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            var source = await store.SourceAsync(account, term, now, token);
            var refusal = Refuse(source, request);
            if (refusal is { } error)
                return error;
            if (await TargetAsync(proof, request.TargetPlanVersionId, token) is not { } offer)
                return CapacityChangeQuoteErrors.Invalid;
            var built = await BuildAsync(account, source!, offer, request, now, token);
            if (built.IsError)
                return built.Errors;
            var (quote, pin, revision) = built.Value;
            await store.SaveAsync(quote, source!, pin, revision, proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return new CapacityChangeQuoteCreation(quote, true);
        }, ct);

    private static Error? Refuse(CapacityChangeSource? source, CreateCapacityChangeQuoteV1 request)
    {
        if (source is null)
            return CapacityChangeQuoteErrors.Missing;
        if (source.Superseded || source.Capacity != request.ObservedCapacity || source.EndsAt != request.ObservedPaidThrough)
            return CapacityChangeQuoteErrors.Changed;
        if (!source.CompleteEvidence || source.Closed || source.Shops.Length is 0 || source.Shops.Length > source.Capacity ||
            source.Shops.Any(s => s.Name.Length is 0 or > CheckoutOrdersV1.MaxShopName))
            return CapacityChangeQuoteErrors.Review;
        return source.Trial || source is { MemberAllowance: > 0, PricePaise: >= 0, Currency: "INR", PlanVersionId: not null }
            ? null : CapacityChangeQuoteErrors.Review;
    }

    private async Task<PublishedPlanTermsV1?> TargetAsync(VerifiedBillingDelegation proof, Guid version, CancellationToken ct) =>
        approval.PublicFixtureFilter is { } filter &&
        await catalogue.ReadVersionAsync(proof.ReplayId, version, filter, true, ct) is
            { AvailableForNewPurchase: true, Terms: { Kind: "single-shop" or "multi-shop" } offer }
            ? offer : null;

    private async Task<ErrorOr<(CapacityChangeQuoteV1, PaymentProviderPin?, long?)>> BuildAsync(Guid account,
        CapacityChangeSource source, PublishedPlanTermsV1 offer, CreateCapacityChangeQuoteV1 request, DateTimeOffset now,
        CancellationToken ct)
    {
        var period = source.Period ?? (offer.MonthlyPricePaise is not null ? "Month" : "Year");
        var target = TargetGross(offer, period, request.TargetCapacity);
        var priorAllowance = source.MemberAllowance ?? 0;
        // A trial converts to any full paid package at least as large; a paid term must strictly grow.
        var grows = source.Trial
            ? request.TargetCapacity >= source.Capacity && offer.MembersPerShop >= priorAllowance
            : CapacityChangeQuotePolicy.IsIncrease(source.Capacity, request.TargetCapacity, priorAllowance, offer.MembersPerShop);
        if (target is null || offer.Currency != "INR" || !grows)
            return CapacityChangeQuoteErrors.Invalid;

        var timing = CapacityChangeQuotePolicy.Timing(source.StartsAt, source.EndsAt, now, source.Trial, GraceEligible(source));
        if (timing == CapacityChangeTiming.NotStarted)
            return CapacityChangeQuoteErrors.Changed;
        var draft = new CapacityChangeDraft(account, source, offer, period, request.TargetCapacity, target.Value, now);
        if (CapacityChangeQuotePolicy.ForTiming(timing) is { } deferred)
            return (CapacityChangeQuoteFactory.Build(draft, deferred, null), null, null);
        if (source.PricePaise is not { } purchased || ProrationPolicy.Prorate(new(purchased, target.Value, source.StartsAt,
                source.EndsAt, now)) is not { } proration)
            return CapacityChangeQuoteErrors.Review;

        var (decision, pin, revision) = await ProviderAsync(proration, ct);
        return (CapacityChangeQuoteFactory.Build(draft, CapacityChangeQuotePolicy.ForCharge(proration, decision), proration),
            pin, revision);
    }

    private static bool GraceEligible(CapacityChangeSource source) =>
        source.Basis == "Paid" && source.CancellationRevision % 2 == 0;

    private static long? TargetGross(PublishedPlanTermsV1 offer, string period, int capacity)
    {
        var monthly = period == "Month";
        var price = CheckoutQuotePolicy.Price(offer.IncludedShops, Math.Max(0, capacity - offer.IncludedShops), capacity,
            monthly ? offer.MonthlyPricePaise : offer.AnnualPricePaise,
            offer.ExtraShopsOffered ? monthly ? offer.ExtraShopMonthlyPricePaise : offer.ExtraShopAnnualPricePaise : null);
        return price is { } full && full.Capacity == capacity ? full.Amount : null;
    }

    /// <summary>Only a positive rounded charge consults the selected provider; the exact pin is frozen with it.</summary>
    private async Task<(ProviderAmountDecision, PaymentProviderPin?, long?)> ProviderAsync(ProrationResult proration,
        CancellationToken ct)
    {
        if (proration.ChargePaise <= 0)
            return (ProviderAmountDecision.Unavailable, null, null);
        var selected = await selection.SelectedAsync(false, ct);
        if (selected.Pin is not { } pin || gateways.ForPin(pin) is not { } gateway || gateway.Pin != pin ||
            !gateway.Capabilities.FullPaymentOnly)
            return (ProviderAmountDecision.Unavailable, null, null);
        if (gateway.SupportsAmount(proration.ChargePaise, "INR"))
            return (ProviderAmountDecision.Supported, pin, selected.Revision);
        var decision = gateway.MinimumAmountPaise("INR") is { } minimum && proration.ChargePaise < minimum
            ? ProviderAmountDecision.BelowMinimum : ProviderAmountDecision.Unsupported;
        return (decision, null, null);
    }
}
