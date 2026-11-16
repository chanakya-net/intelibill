using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

/// <summary>Upstream status answers are relayed only when they are internally coherent and bound to the request.</summary>
internal static class SubscriptionStatusContract
{
    internal static bool IsValidShop(ShopSubscriptionStatusV1 status, Guid shop) =>
        status is { SchemaVersion: SubscriptionStatusV1.SchemaVersion, ObserveOnly: true }
        && status.ShopId == shop && Common(status.EntitlementSynchronization, status.PaymentVerification)
        && SubscriptionStatusV1.States.Contains(status.State)
        && (status.Term is null ? status.State == "PendingActivation" : status.State == status.Term.State && IsValidTerm(status.Term))
        && (status.AccountId is null || status.AccountId != Guid.Empty)
        && (status.Term?.Commercial is null || status.AccountId is not null);

    internal static bool IsValidAccount(AccountSubscriptionStatusV1 status, Guid account, Guid? after)
    {
        if (status is not { SchemaVersion: SubscriptionStatusV1.SchemaVersion, ObserveOnly: true }
            || status.AccountId != account || status.Items is not { Length: <= SubscriptionStatusV1.PageSize } items
            || !Common(status.EntitlementSynchronization, status.PaymentVerification))
            return false;
        var previous = after;
        foreach (var item in items)
        {
            if (item?.Status is not { } term || !IsValidTerm(term) || item.CoveredShops is not { } shops
                || shops.Length > SubscriptionStatusV1.MaximumCoveredShops || item.CoveredShopCount < shops.Length
                || (previous is { } prior && term.TermId.CompareTo(prior) <= 0))
                return false;
            previous = term.TermId;
        }
        return status.NextCursor is null || (items.Length == SubscriptionStatusV1.PageSize && status.NextCursor == previous);
    }

    private static bool Common(string synchronization, string verification) =>
        synchronization == SubscriptionStatusV1.NotAvailable && verification == SubscriptionStatusV1.NotAvailable;

    private static bool IsValidTerm(SubscriptionTermStatusV1 term) =>
        term.TermId != Guid.Empty && SubscriptionStatusV1.States.Contains(term.State)
        && SubscriptionStatusV1.EvidenceStates.Contains(term.EvidenceState) && term.TimeZone == "Asia/Kolkata"
        && (term.Basis is null || SubscriptionStatusV1.Bases.Contains(term.Basis)) && term.PaidThrough > term.StartsAt
        && (term.GraceEndsAt is null || term.GraceEndsAt > term.PaidThrough) && term.PurchasedCapacity >= 1
        && term.CoverageMode is "Independent" or "Grouped" && term.MemberAllowance is null or >= 1
        && (term.Commercial is null || term.Commercial.PricePaise is null or >= 0);
}
