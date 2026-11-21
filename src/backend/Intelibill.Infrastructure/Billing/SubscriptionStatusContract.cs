using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

/// <summary>Upstream status answers are relayed only when they are internally coherent and bound to the request.</summary>
internal static class SubscriptionStatusContract
{
    internal static bool IsValidShop(ShopSubscriptionStatusV1 status, Guid shop) =>
        status is { SchemaVersion: SubscriptionStatusV1.SchemaVersion, ObserveOnly: true, EnforcedSuspensionMode: null }
        && status is { ReopeningEnforced: null, ReopeningReceived: null, ReopenedVersion: null }
        && (status.ReopenedAt is { } reopened
            ? EntitlementSnapshotsV1.IsInstant(reopened) && status.ReopeningRequiredRevision > 1
            : status.ReopeningRequiredRevision is null)
        && status.ShopId == shop && status.PaymentVerification == SubscriptionStatusV1.NotAvailable
        && status.EntitlementSynchronization == (status.Synchronization?.State ?? SubscriptionStatusV1.NotAvailable)
        && IsValidSynchronization(status.Synchronization)
        && (status.Suspension is null || status.Suspension.IsValid())
        && SubscriptionStatusV1.States.Contains(status.State)
        && (status.Term is null ? status.State == "PendingActivation" : status.State == status.Term.State && IsValidTerm(status.Term))
        && (status.AccountId is null || status.AccountId != Guid.Empty)
        && (status.Term?.Commercial is null || status.AccountId is not null);

    internal static bool IsValidAccount(AccountSubscriptionStatusV1 status, Guid account, Guid? after)
    {
        if (status is not { SchemaVersion: SubscriptionStatusV1.SchemaVersion, ObserveOnly: true }
            || status.AccountId != account || status.Items is not { Length: <= SubscriptionStatusV1.PageSize } items
            || status.PaymentVerification != SubscriptionStatusV1.NotAvailable)
            return false;
        var previous = after;
        foreach (var item in items)
        {
            if (item?.Status is not { } term || !IsValidTerm(term) || item.CoveredShops is not { } shops
                || shops.Length > SubscriptionStatusV1.MaximumCoveredShops || item.CoveredShopCount < shops.Length
                || shops.Any(shop => shop is null || !IsValidSynchronization(shop.Synchronization))
                || (previous is { } prior && term.TermId.CompareTo(prior) <= 0))
                return false;
            previous = term.TermId;
        }
        return status.EntitlementSynchronization == SubscriptionStatusV1.Summarize(items.SelectMany(item => item.CoveredShops))
            && (status.NextCursor is null || (items.Length == SubscriptionStatusV1.PageSize && status.NextCursor == previous));
    }

    private static bool IsValidSynchronization(EntitlementSynchronizationV1? sync)
    {
        if (sync is null) return true;
        if (sync.SavedAccountRevision <= 0 || sync.SavedShopRevision <= 0 ||
            !EntitlementSnapshotsV1.IsInstant(sync.SavedVerifiedAt) || !HasCoherentConfirmation(sync))
            return false;

        return sync.State switch
        {
            "Pending" => sync.Attention is null && sync.Delivery is "Queued" or "Retrying" or "AwaitingAcknowledgement",
            "Applied" => sync.Delivery == "Acknowledged" && sync.Attention is null &&
                sync.AppliedAccountRevision >= sync.SavedAccountRevision && sync.AppliedShopRevision >= sync.SavedShopRevision,
            "NeedsReview" => sync.Delivery switch
            {
                "Acknowledged" => SubscriptionStatusV1.ReviewDispositions.Contains(sync.Attention ?? ""),
                "DeadLetter" => SubscriptionStatusV1.DeliveryFailures.Contains(sync.Attention ?? ""),
                _ => false,
            },
            _ => false,
        };
    }

    private static bool HasCoherentConfirmation(EntitlementSynchronizationV1 sync) =>
        sync.LastSynchronizedAt is { } at
            ? sync.AppliedAccountRevision > 0 && sync.AppliedShopRevision > 0 && EntitlementSnapshotsV1.IsInstant(at)
            : sync.AppliedAccountRevision is null && sync.AppliedShopRevision is null;

    private static bool IsValidTerm(SubscriptionTermStatusV1 term) =>
        term.TermId != Guid.Empty && SubscriptionStatusV1.States.Contains(term.State)
        && SubscriptionStatusV1.EvidenceStates.Contains(term.EvidenceState) && term.TimeZone == "Asia/Kolkata"
        && (term.Basis is null || SubscriptionStatusV1.Bases.Contains(term.Basis)) && term.PaidThrough > term.StartsAt
        && (term.GraceEndsAt is null || term.GraceEndsAt > term.PaidThrough) && term.PurchasedCapacity >= 1
        && term.CoverageMode is "Independent" or "Grouped" && term.MemberAllowance is null or >= 1
        && (term.Commercial is null || term.Commercial.PricePaise is null or >= 0)
        && (term.Cancellation is null || (term.Basis == "Paid" && term.GraceEndsAt is null && term.EvidenceState == "Complete"
            && term.State is "Scheduled" or "Active" or "Expired" or "Suspended"
            && term.AccessEndsAt == term.PaidThrough && MerchantBillingClient.IsCancellationValid(term.Cancellation, term.PaidThrough)));
}
