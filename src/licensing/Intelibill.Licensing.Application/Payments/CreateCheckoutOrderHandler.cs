using System.Globalization;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record CheckoutQuoteCreation(CheckoutQuoteV1 Quote, bool Created);

public sealed class CreateCheckoutOrderHandler(IBillingDelegationAccess access, ICheckoutOrderStore store,
    IPublishedCatalogueReader catalogue, ICataloguePublicationApproval approval,
    TimeProvider clock)
{
    public Task<ErrorOr<CheckoutQuoteCreation>> HandleAsync(VerifiedBillingDelegation proof, Guid account,
        CreateCheckoutQuoteV1 request, CancellationToken ct) =>
        access.ExecuteAsync<CheckoutQuoteCreation>(proof, async (_, token) =>
        {
            if (!request.IsValid())
                return CheckoutOrderErrors.Invalid;
            var hash = CheckoutOrdersV1.CreateHash(request);
            var prior = await store.FindAsync(account, request.OperationKey, byOperation: true, token);
            if (prior is not null)
                return prior.ActorId == proof.ActorId && prior.RequestHash == hash
                    ? new CheckoutQuoteCreation(prior.Quote, false) : BillingAccountErrors.PayloadMismatch;
            if (request.ExpectedRevision != 1)
                return CheckoutOrderErrors.Changed;
            if (approval.PublicFixtureFilter is not { } filter ||
                await catalogue.ReadVersionAsync(proof.ReplayId, request.PlanVersionId, filter, true, token) is
                    not { AvailableForNewPurchase: true, Terms: { } offer })
                return CheckoutOrderErrors.Invalid;

            var source = await store.SourceAsync(account, request.TermId, request.ShopIds!, token);
            var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            if (source?.Prior is { ActiveReductionId: not null } priorTerm &&
                (priorTerm.BlocksRestoredAccess || now < priorTerm.EndsAt + GracePolicy.EligiblePaidGrace))
                return CheckoutOrderErrors.Changed;
            if (source is null || BuildQuote(account, offer, request, source, now) is not { } quote)
                return CheckoutOrderErrors.Invalid;
            if (CheckoutRenewalPolicy.IsRenewal(quote) && await store.ReplacementAsync(account, request.TermId!.Value,
                    quote.Capacity, quote.MembersPerShop, quote.PreviewStartsAt, now, token) is { } replacement)
            {
                quote = quote with { Replacement = replacement, ShopHash = CheckoutOrdersV1.ShopHash(source.OrderShopIds(replacement)) };
                quote = quote with { SnapshotHash = CheckoutOrdersV1.SnapshotHash(quote) };
            }
            if (await store.HasConflictingPaidCoverageAsync(account, source.OrderShopIds(quote.Replacement),
                    request.TermId, quote.PreviewStartsAt, quote.PreviewEndsAt, token))
                return CheckoutOrderErrors.Invalid;
            await store.SaveAsync(quote, source, proof.ActorId, request.OperationKey, hash, proof.ReplayId, token);
            return new CheckoutQuoteCreation(quote, true);
        }, ct);

    private static CheckoutQuoteV1? BuildQuote(Guid account, PublishedPlanTermsV1 offer, CreateCheckoutQuoteV1 request,
        CheckoutSource source, DateTimeOffset now)
    {
        var monthly = request.Period == "Month";
        var named = source.Shops.Length;
        var chargedCapacity = source.Prior?.Capacity ?? named;
        var extras = source.Prior is null ? request.ExtraShops : Math.Max(0, chargedCapacity - offer.IncludedShops);
        var price = CheckoutQuotePolicy.Price(offer.IncludedShops, extras, chargedCapacity,
            monthly ? offer.MonthlyPricePaise : offer.AnnualPricePaise,
            offer.ExtraShopsOffered ? monthly ? offer.ExtraShopMonthlyPricePaise : offer.ExtraShopAnnualPricePaise : null);
        if (price is not { } full || offer.Kind is not ("single-shop" or "multi-shop") ||
            (source.Prior is not null && request.ExtraShops != 0) || named > full.Capacity ||
            source.Shops.Any(s => s.AppliedAt > now || s.Name.Length is 0 or > CheckoutOrdersV1.MaxShopName))
            return null;
        if (Preview(request, source, full.Capacity, now) is not { Anchor: { } anchor, Ordinal: { } ordinal,
                StartsAt: { } starts, EndsAt: { } ends } dates)
            return null;

        var quote = new CheckoutQuoteV1(account, Guid.NewGuid(), 1, offer.PlanId, offer.VersionId, offer.VersionNumber, offer.Name,
            request.Period, request.TermId, full.Capacity, offer.MembersPerShop, source.Shops.Length,
            CheckoutOrdersV1.ShopHash(source.Shops.Select(s => s.ShopId)), source.Hash(), full.Amount, offer.Currency,
            offer.PriceBasis, offer.CustomerTermsVersion, offer.RefundPolicyVersion, BillingCalendar.Zone, BillingCalendar.Version,
            RenewalPolicy.Version, dates.Start.ToString(), starts, ends, dates.IncludedGraceDays,
            anchor.LocalStart.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ordinal, now, now + CheckoutQuotePolicy.Lifetime, offer.DevelopmentFixture, string.Empty,
            FreshStart: dates.Start == RenewalStart.PriorPaidThrough ? null : source.FreshStart());
        return quote with { SnapshotHash = CheckoutOrdersV1.SnapshotHash(quote) };
    }

    private static RenewalDecision? Preview(CreateCheckoutQuoteV1 request, CheckoutSource source, int capacity, DateTimeOffset now)
    {
        var period = request.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        if (source.Prior is not { } prior)
            return RenewalPolicy.DetermineTerm(new(period, now, RoundFreshStartUp: true));
        if (prior.Basis != "Paid" || prior.Period != request.Period || prior.Capacity != capacity ||
            prior.CalendarVersion != BillingCalendar.Version || prior.AccessPolicyVersion != GracePolicy.Version ||
            (prior.StartsAt > now && !prior.Renewed) || prior.CancellationRevision % 2 == 1 ||
            source.Shops.Any(s => !ValidCoverageEnd(s, prior)))
            return null;

        var anchor = new BillingAnchor(prior.AnchorLocal, BillingCalendar.Zone, period);
        if (BillingCalendar.TryBoundary(anchor, prior.Ordinal) != prior.StartsAt ||
            BillingCalendar.TryBoundary(anchor, prior.Ordinal + 1) != prior.EndsAt ||
            BillingCalendar.TryBoundary(anchor, prior.Ordinal + 2) is null)
            return null;
        var decision = RenewalPolicy.DetermineTerm(new(period, now, SubscriptionBasis.Paid, prior.EndsAt, anchor, prior.Ordinal,
            RestoredAccessAt: now, RoundFreshStartUp: true, RefundAccessEndsAt: prior.RefundAccessEndsAt));
        if (prior.ActiveReductionId is not null &&
            (decision.Start != RenewalStart.RestoredAccess || prior.BlocksRestoredAccess))
            return null;
        return decision;
    }

    private static bool ValidCoverageEnd(CheckoutSourceShop shop, CheckoutRenewalSource prior) =>
        prior.RefundAccessEndsAt is null
            ? shop.CoverageEndsAt == prior.EndsAt
            : shop.CoverageEndsAt is { } end && end > prior.StartsAt && end <= prior.EndsAt;
}
