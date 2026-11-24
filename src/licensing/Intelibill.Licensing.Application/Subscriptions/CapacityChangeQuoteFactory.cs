using System.Globalization;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

internal sealed record CapacityChangeDraft(Guid Account, CapacityChangeSource Source, PublishedPlanTermsV1 Offer,
    string Period, int TargetCapacity, long TargetGrossPaise, DateTimeOffset Now);

/// <summary>Assembles the immutable public quote from frozen source, target and decision facts.</summary>
internal static class CapacityChangeQuoteFactory
{
    internal static CapacityChangeQuoteV1 Build(CapacityChangeDraft d, CapacityChangeDecision decision, ProrationResult? proration)
    {
        var s = d.Source;
        var expires = d.Now + CheckoutQuotePolicy.Lifetime;
        var payable = decision.Payable && proration is not null;
        var quote = new CapacityChangeQuoteV1(d.Account, Guid.NewGuid(), s.TermId, decision.Outcome, decision.Reason,
            decision.Actions, s.PlanVersionId, d.Offer.PlanId, d.Offer.VersionId, d.Offer.VersionNumber, d.Offer.Name,
            d.Period, s.Capacity, d.TargetCapacity, s.MemberAllowance ?? 0, d.Offer.MembersPerShop, s.Shops.Length,
            CheckoutOrdersV1.ShopHash(s.Shops.Select(shop => shop.ShopId)), s.Hash(), s.Trial ? null : s.PricePaise,
            d.TargetGrossPaise, proration?.DifferencePaise, payable ? proration!.ChargePaise : null, "INR",
            d.Offer.PriceBasis, s.StartsAt, s.EndsAt, d.Now, proration?.TotalSeconds, proration?.RemainingSeconds,
            s.AnchorLocal?.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), s.Ordinal, BillingCalendar.Zone,
            ProrationPolicy.Version, BillingCalendar.Version, s.Suspended, d.Now, expires,
            payable && s.EndsAt < expires ? s.EndsAt : expires, d.Offer.DevelopmentFixture || s.DevelopmentFixture, string.Empty);
        return quote with { SnapshotHash = CapacityChangeQuotesV1.SnapshotHash(quote) };
    }
}
