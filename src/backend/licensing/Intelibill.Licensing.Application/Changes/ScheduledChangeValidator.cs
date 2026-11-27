using System.Globalization;
using ErrorOr;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Changes;

public static class CapacityReductionErrors
{
    public static Error Invalid => Error.Validation(CapacityReductionsV1.Invalid,
        "Choose a smaller published package and an explicit non-empty set of retained shops.");
    public static Error NotFound => Error.NotFound(CapacityReductionsV1.NotFound,
        "The subscription term or scheduled reduction was not found.");
    public static Error SourceChanged => Error.Conflict(CapacityReductionsV1.SourceChanged,
        "The subscription coverage or active counts changed. Review the current shops before confirming.");
    public static Error Expired => Error.Conflict(CapacityReductionsV1.Expired,
        "This reduction preview expired. Request a new preview.");
    public static Error MemberConflict => Error.Conflict(CapacityReductionsV1.MemberConflict,
        "One or more retained shops exceed the target member allowance. Resolve active memberships first.");
    public static Error AlreadyScheduled => Error.Conflict(CapacityReductionsV1.AlreadyScheduled,
        "A capacity reduction is already scheduled for this subscription term.");
    public static Error AlreadyFunded => Error.Conflict(CapacityReductionsV1.AlreadyFunded,
        "This scheduled reduction is already funded and awaiting renewal.");
    public static Error NeedsReview => Error.Conflict(CapacityReductionsV1.NeedsReview,
        "This subscription needs provider review before a reduction can be scheduled.");
    public static Error Unavailable => Error.Failure(CapacityReductionsV1.Unavailable,
        "Live shop membership counts are temporarily unavailable.");
}

public sealed record ReductionTargetTerms(
    PublishedPlanTermsV1 Offer, string Period, int TargetCapacity, string TargetMode, long AmountPaise,
    BillingAnchor Anchor, int NextOrdinal, DateTimeOffset NextStartsAt, DateTimeOffset NextEndsAt);

/// <summary>
/// Pure validation rules for owner-selected renewal reductions: strictly lowers slot count or per-shop member
/// allowance without growing either, requires an explicit non-empty subset of covered shops and live counts
/// within the target allowance. Never selects shops or alters memberships automatically.
/// </summary>
public static class ScheduledChangeValidator
{
    public static bool IsReduction(int priorCapacity, int targetCapacity, int priorAllowance, int targetAllowance) =>
        targetCapacity > 0 && targetAllowance > 0 &&
        targetCapacity <= priorCapacity && targetAllowance <= priorAllowance &&
        (targetCapacity < priorCapacity || targetAllowance < priorAllowance);

    public static bool GraceEligible(CapacityChangeSource source) =>
        source.Basis == "Paid" && source.CancellationRevision % 2 == 0;

    public static DateTimeOffset RenewalWindowEnd(CapacityChangeSource source) =>
        GraceEligible(source) ? source.EndsAt + GracePolicy.EligiblePaidGrace : source.EndsAt;

    public static Error? ValidateSource(CapacityChangeSource? source, DateTimeOffset now,
        int? observedCapacity = null, int? observedAllowance = null, DateTimeOffset? observedPaidThrough = null)
    {
        if (source is null) return CapacityReductionErrors.NotFound;
        if (source.Trial) return CapacityReductionErrors.Invalid;
        if (!source.CompleteEvidence || source.Closed || source.Shops.Length == 0 || source.Shops.Length > source.Capacity ||
            source.Shops.Any(s => s.Name.Length is 0 or > CheckoutOrdersV1.MaxShopName))
            return CapacityReductionErrors.NeedsReview;
        if (source.Basis != "Paid" || source.MemberAllowance is not > 0 || source.PricePaise is not >= 0 ||
            source.Currency != "INR" || source.AnchorLocal is null || source.Ordinal is null ||
            source.Period is not ("Month" or "Year"))
            return CapacityReductionErrors.NeedsReview;
        if (source.Superseded || source.CancellationRevision % 2 == 1 || now < source.StartsAt || now >= RenewalWindowEnd(source))
            return CapacityReductionErrors.SourceChanged;
        if (observedCapacity is { } cap && cap != source.Capacity) return CapacityReductionErrors.SourceChanged;
        if (observedAllowance is { } allow && allow != source.MemberAllowance) return CapacityReductionErrors.SourceChanged;
        if (observedPaidThrough is { } end && end != source.EndsAt) return CapacityReductionErrors.SourceChanged;
        return null;
    }

    public static ErrorOr<ReductionTargetTerms> ValidateTarget(CapacityChangeSource source, PublishedPlanTermsV1? offer, int targetCapacity)
    {
        if (offer is null || offer.Kind is not ("single-shop" or "multi-shop") || offer.Currency != "INR")
            return CapacityReductionErrors.Invalid;
        var priorAllowance = source.MemberAllowance!.Value;
        if (!IsReduction(source.Capacity, targetCapacity, priorAllowance, offer.MembersPerShop))
            return CapacityReductionErrors.Invalid;
        var period = source.Period!;
        var monthly = period == "Month";
        var price = CheckoutQuotePolicy.Price(offer.IncludedShops, Math.Max(0, targetCapacity - offer.IncludedShops), targetCapacity,
            monthly ? offer.MonthlyPricePaise : offer.AnnualPricePaise,
            offer.ExtraShopsOffered ? monthly ? offer.ExtraShopMonthlyPricePaise : offer.ExtraShopAnnualPricePaise : null);
        if (price is not { } full || full.Capacity != targetCapacity)
            return CapacityReductionErrors.Invalid;
        var billingPeriod = period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        var anchor = new BillingAnchor(DateTime.SpecifyKind(source.AnchorLocal!.Value, DateTimeKind.Unspecified),
            BillingCalendar.Zone, billingPeriod);
        var ordinal = source.Ordinal!.Value;
        if (BillingCalendar.TryBoundary(anchor, ordinal) != source.StartsAt ||
            BillingCalendar.TryBoundary(anchor, ordinal + 1) != source.EndsAt ||
            BillingCalendar.TryBoundary(anchor, ordinal + 2) is not { } nextEnds)
            return CapacityReductionErrors.NeedsReview;
        var mode = targetCapacity == 1 ? "Independent" : "Grouped";
        return new ReductionTargetTerms(offer, period, targetCapacity, mode, full.Amount, anchor, ordinal + 1, source.EndsAt, nextEnds);
    }

    public static Error? ValidateSelection(CapacityReductionPreviewV1 preview, CapacityChangeSource source,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> previewShops,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> liveShops, Guid[] retainedIds)
    {
        if (retainedIds.Length == 0 || retainedIds.Length > preview.TargetCapacity)
            return CapacityReductionErrors.Invalid;
        if (retainedIds.Any(id => !previewShops.ContainsKey(id)))
            return CapacityReductionErrors.Invalid;
        if (source.Shops.Length != preview.SourceShopCount || source.Hash() != preview.SourceHash ||
            source.Suspended != preview.SuspensionDisclosed)
            return CapacityReductionErrors.SourceChanged;
        if (liveShops.Count != source.Shops.Length)
            return CapacityReductionErrors.SourceChanged;
        var retained = new HashSet<Guid>(retainedIds);
        foreach (var shop in source.Shops)
        {
            if (!liveShops.TryGetValue(shop.ShopId, out var live) || !live.AffiliationMatched ||
                live.AffiliationRequestId != shop.RequestId)
                return CapacityReductionErrors.SourceChanged;
            if (!previewShops.TryGetValue(shop.ShopId, out var seen) || seen.AffiliationRequestId != shop.RequestId)
                return CapacityReductionErrors.SourceChanged;
            if (retained.Contains(shop.ShopId))
            {
                if (live.ActiveMembers <= 0 || live.ActiveMembers > preview.TargetMemberAllowance)
                    return CapacityReductionErrors.MemberConflict;
                if (live.ActiveMembers != seen.ActiveMembers || live.SeatHash != seen.SeatHash)
                    return CapacityReductionErrors.SourceChanged;
            }
        }
        return null;
    }

    public static string AnchorText(BillingAnchor anchor) =>
        anchor.LocalStart.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}
