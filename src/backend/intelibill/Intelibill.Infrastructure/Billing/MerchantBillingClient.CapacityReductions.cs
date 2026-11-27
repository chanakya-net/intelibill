using System.Globalization;
using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCapacityReductions
{
    public Task<ErrorOr<CapacityReductionPreviewV1>> CreatePreviewAsync(MerchantBillingCaller caller, Guid account,
        Guid term, CreateCapacityReductionPreviewV1 request, CancellationToken ct) =>
        RunAsync<CapacityReductionPreviewV1>(caller, new(CapacityReductionsV1.Preview, HttpMethod.Post,
            CapacityReductionsV1.PreviewsPath(account, term), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            CapacityReductionsV1.MaxDetailBytes, async (response, token) =>
            {
                var preview = await ReadJsonAsync<CapacityReductionPreviewV1>(response, token);
                return preview is not null && IsReductionPreviewValid(preview, account, term) &&
                    preview.TargetPlanVersionId == request.TargetPlanVersionId &&
                    preview.TargetCapacity == request.TargetCapacity &&
                    preview.PriorCapacity == request.ObservedCapacity &&
                    preview.PriorMemberAllowance == request.ObservedMemberAllowance &&
                    preview.CurrentPaidThrough == request.ObservedPaidThrough.ToUniversalTime()
                    ? preview : Unavailable("InvalidReductionPreview");
            }, ct);

    public Task<ErrorOr<CapacityReductionShopPageV1>> ReadPreviewShopsAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid previewId, Guid? after, CancellationToken ct) =>
        RunAsync<CapacityReductionShopPageV1>(caller, new(CapacityReductionsV1.PreviewShops, HttpMethod.Get,
            CapacityReductionsV1.PreviewShopsPath(account, term, previewId, after), account, []),
            CapacityReductionsV1.MaxPageBytes, async (response, token) =>
            {
                var page = await ReadJsonAsync<CapacityReductionShopPageV1>(response, token);
                return page is not null && IsReductionShopPageValid(page, account, previewId, after, expectRetained: false)
                    ? page : Unavailable("InvalidReductionPreviewShops");
            }, ct);

    public Task<ErrorOr<MerchantScheduledReduction>> ScheduleAsync(MerchantBillingCaller caller, Guid account, Guid term,
        ScheduleCapacityReductionV1 request, CancellationToken ct) =>
        RunAsync<MerchantScheduledReduction>(caller, new(CapacityReductionsV1.Schedule, HttpMethod.Post,
            CapacityReductionsV1.BasePath(account, term), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            CapacityReductionsV1.MaxDetailBytes, async (response, token) =>
            {
                var schedule = await ReadJsonAsync<ScheduledCapacityReductionV1>(response, token);
                var created = response.StatusCode == HttpStatusCode.Created;
                return schedule is not null && IsScheduledReductionValid(schedule, account, term) &&
                    schedule.PreviewId == request.PreviewId &&
                    schedule.RetainedShopCount == request.RetainedShopIds.Length &&
                    schedule.RetainedShopHash == CheckoutOrdersV1.ShopHash(request.RetainedShopIds)
                    ? new MerchantScheduledReduction(schedule, created) : Unavailable("InvalidScheduledReduction");
            }, ct);

    public Task<ErrorOr<ScheduledCapacityReductionV1>> ReadScheduleAsync(MerchantBillingCaller caller, Guid account,
        Guid term, CancellationToken ct) =>
        ReadScheduledAsync(caller, new(CapacityReductionsV1.Read, HttpMethod.Get,
            CapacityReductionsV1.BasePath(account, term), account, []), account, term, ct);

    public Task<ErrorOr<ScheduledCapacityReductionV1>> RecoverScheduleAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid operationKey, CancellationToken ct) =>
        ReadScheduledAsync(caller, new(CapacityReductionsV1.Recover, HttpMethod.Get,
            CapacityReductionsV1.RecoverPath(account, term, operationKey), account, []), account, term, ct);

    public Task<ErrorOr<CapacityReductionShopPageV1>> ReadScheduleShopsAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid scheduleId, Guid? after, CancellationToken ct) =>
        RunAsync<CapacityReductionShopPageV1>(caller, new(CapacityReductionsV1.Shops, HttpMethod.Get,
            CapacityReductionsV1.ShopsPath(account, term, scheduleId, after), account, []),
            CapacityReductionsV1.MaxPageBytes, async (response, token) =>
            {
                var page = await ReadJsonAsync<CapacityReductionShopPageV1>(response, token);
                return page is not null && IsReductionShopPageValid(page, account, scheduleId, after, expectRetained: true)
                    ? page : Unavailable("InvalidReductionShops");
            }, ct);

    public Task<ErrorOr<CheckoutQuoteV1>> CreateReductionOrderAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid scheduleId, CreateReductionOrderV1 request, CancellationToken ct) =>
        RunAsync<CheckoutQuoteV1>(caller, new(CheckoutOrdersV1.Create, HttpMethod.Post,
            CapacityReductionsV1.OrderPath(account, term, scheduleId), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            CheckoutOrdersV1.MaxQuoteBytes, async (response, token) =>
            {
                var order = await ReadJsonAsync<CheckoutQuoteV1>(response, token);
                return order is not null && IsCheckoutQuoteValid(order, account) && order.TermId == term &&
                    order.Reduction is { } red && red.ScheduleId == scheduleId && red.ScheduleHash == request.SnapshotHash
                    ? order : Unavailable("InvalidReductionOrder");
            }, ct);

    public Task<ErrorOr<ScheduledCapacityReductionV1>> ResolveScheduleAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid scheduleId, ResolveCapacityReductionV1 request, CancellationToken ct) =>
        RunAsync<ScheduledCapacityReductionV1>(caller, new(CapacityReductionsV1.Resolve, HttpMethod.Post,
            CapacityReductionsV1.ResolvePath(account, term, scheduleId), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            CapacityReductionsV1.MaxDetailBytes, async (response, token) =>
            {
                var schedule = await ReadJsonAsync<ScheduledCapacityReductionV1>(response, token);
                return schedule is not null && IsScheduledReductionValid(schedule, account, term) &&
                    schedule.ScheduleId == scheduleId && schedule.SnapshotHash == request.SnapshotHash
                    ? schedule : Unavailable("InvalidScheduledReduction");
            }, ct);

    private Task<ErrorOr<ScheduledCapacityReductionV1>> ReadScheduledAsync(MerchantBillingCaller caller,
        BillingDelegationIntent intent, Guid account, Guid term, CancellationToken ct) =>
        RunAsync<ScheduledCapacityReductionV1>(caller, intent, CapacityReductionsV1.MaxDetailBytes, async (response, token) =>
        {
            var schedule = await ReadJsonAsync<ScheduledCapacityReductionV1>(response, token);
            return schedule is not null && IsScheduledReductionValid(schedule, account, term)
                ? schedule : Unavailable("InvalidScheduledReduction");
        }, ct);

    internal static bool IsReductionPreviewValid(CapacityReductionPreviewV1 p, Guid account, Guid term) =>
        p.SchemaVersion == 1 && p.AccountId == account && p.TermId == term && p.PreviewId != Guid.Empty &&
        p.TargetPlanId != Guid.Empty && p.TargetPlanVersionId != Guid.Empty && p.TargetPlanVersion > 0 &&
        p.TargetPlanName is { Length: > 0 and <= 80 } && p.Period is "Month" or "Year" &&
        p.PriorCapacity is > 0 and <= CheckoutOrdersV1.MaxShops &&
        p.TargetCapacity is > 0 && p.TargetCapacity <= p.PriorCapacity &&
        p.PriorMemberAllowance is > 0 and <= CheckoutOrdersV1.MaxShops &&
        p.TargetMemberAllowance is > 0 && p.TargetMemberAllowance <= p.PriorMemberAllowance &&
        (p.TargetCapacity < p.PriorCapacity || p.TargetMemberAllowance < p.PriorMemberAllowance) &&
        p.TargetMode is "Independent" or "Grouped" &&
        p.SourceShopCount > 0 && p.SourceShopCount <= p.PriorCapacity &&
        p.SlotsRemoved == p.PriorCapacity - p.TargetCapacity &&
        p.UnusedSlots == Math.Max(0, p.PriorCapacity - p.SourceShopCount) &&
        p.MinimumShopsToRemove == Math.Max(0, p.SourceShopCount - p.TargetCapacity) &&
        CheckoutOrdersV1.IsHash(p.SourceShopHash) && CheckoutOrdersV1.IsHash(p.SourceHash) &&
        CheckoutOrdersV1.IsHash(p.ObservationDigest) &&
        p.AmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise && p.Currency == "INR" &&
        p.PriceBasis == "TaxInclusiveFinal" && p.BillingTimeZone == "Asia/Kolkata" &&
        p.CalendarVersion == "calendar-month-year/1" && p.RenewalPolicyVersion == "renewal/1" &&
        DateTime.TryParseExact(p.AnchorLocal, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
        p.Ordinal is > 0 and <= 1201 && EntitlementSnapshotsV1.IsInstant(p.CurrentStartsAt) &&
        EntitlementSnapshotsV1.IsInstant(p.CurrentPaidThrough) && EntitlementSnapshotsV1.IsInstant(p.RenewalStartsAt) &&
        EntitlementSnapshotsV1.IsInstant(p.RenewalEndsAt) && p.CurrentStartsAt < p.CurrentPaidThrough &&
        p.RenewalStartsAt == p.CurrentPaidThrough && p.RenewalStartsAt < p.RenewalEndsAt &&
        EntitlementSnapshotsV1.IsInstant(p.CreatedAt) && EntitlementSnapshotsV1.IsInstant(p.ExpiresAt) &&
        p.ExpiresAt - p.CreatedAt == TimeSpan.FromMinutes(15) &&
        p.SnapshotHash == CapacityReductionsV1.PreviewSnapshotHash(p);

    internal static bool IsScheduledReductionValid(ScheduledCapacityReductionV1 s, Guid account, Guid term) =>
        s.SchemaVersion == 1 && s.AccountId == account && s.TermId == term && s.ScheduleId != Guid.Empty &&
        s.PreviewId != Guid.Empty && CapacityReductionsV1.States.Contains(s.State) &&
        s.TargetPlanId != Guid.Empty && s.TargetPlanVersionId != Guid.Empty && s.TargetPlanVersion > 0 &&
        s.TargetPlanName is { Length: > 0 and <= 80 } && s.Period is "Month" or "Year" &&
        s.PriorCapacity is > 0 and <= CheckoutOrdersV1.MaxShops &&
        s.TargetCapacity is > 0 && s.TargetCapacity <= s.PriorCapacity &&
        s.PriorMemberAllowance is > 0 and <= CheckoutOrdersV1.MaxShops &&
        s.TargetMemberAllowance is > 0 && s.TargetMemberAllowance <= s.PriorMemberAllowance &&
        (s.TargetCapacity < s.PriorCapacity || s.TargetMemberAllowance < s.PriorMemberAllowance) &&
        s.TargetMode is "Independent" or "Grouped" &&
        s.SourceShopCount > 0 && s.SourceShopCount <= s.PriorCapacity &&
        s.RetainedShopCount > 0 && s.RetainedShopCount <= s.TargetCapacity && s.RetainedShopCount <= s.SourceShopCount &&
        CheckoutOrdersV1.IsHash(s.SourceShopHash) && CheckoutOrdersV1.IsHash(s.RetainedShopHash) &&
        CheckoutOrdersV1.IsHash(s.SourceHash) && CheckoutOrdersV1.IsHash(s.ObservationDigest) &&
        s.AmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise && s.Currency == "INR" &&
        s.PriceBasis == "TaxInclusiveFinal" && s.BillingTimeZone == "Asia/Kolkata" &&
        s.CalendarVersion == "calendar-month-year/1" && s.RenewalPolicyVersion == "renewal/1" &&
        DateTime.TryParseExact(s.AnchorLocal, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
        s.Ordinal is > 0 and <= 1201 && s.ResolutionGeneration >= 0 &&
        EntitlementSnapshotsV1.IsInstant(s.CurrentStartsAt) && EntitlementSnapshotsV1.IsInstant(s.CurrentPaidThrough) &&
        EntitlementSnapshotsV1.IsInstant(s.EffectiveAt) && EntitlementSnapshotsV1.IsInstant(s.RenewalEndsAt) &&
        s.CurrentStartsAt < s.CurrentPaidThrough && s.EffectiveAt == s.CurrentPaidThrough &&
        s.EffectiveAt < s.RenewalEndsAt && EntitlementSnapshotsV1.IsInstant(s.CreatedAt) &&
        (s.Conflicts is null || s.Conflicts.Length <= CapacityReductionsV1.MaxInlineConflicts) &&
        s.SnapshotHash == CapacityReductionsV1.ScheduleSnapshotHash(s);

    private static bool IsReductionShopPageValid(CapacityReductionShopPageV1 page, Guid account, Guid referenceId,
        Guid? after, bool expectRetained)
    {
        if (page.AccountId != account || page.ReferenceId != referenceId ||
            page.Items is not { Length: <= CapacityReductionsV1.PageSize } items)
            return false;
        var prior = after ?? Guid.Empty;
        foreach (var shop in items)
        {
            if (shop is not { Name.Length: > 0 and <= CheckoutOrdersV1.MaxShopName } ||
                shop.ShopId.CompareTo(prior) <= 0 || shop.AffiliationRequestId == Guid.Empty ||
                shop.ActiveMembers < 0 || shop.ExcessMembers < 0 ||
                (expectRetained ? shop.Retained is null : shop.Retained is not null))
                return false;
            prior = shop.ShopId;
        }
        return page.NextCursor is null || (items.Length == CapacityReductionsV1.PageSize && page.NextCursor == prior);
    }

    private Error MapCapacityReductionFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, CapacityReductionsV1.Invalid) => Error.Validation(code,
            "Choose a lower published renewal package and explicitly select the retained shops."),
        (HttpStatusCode.NotFound, CapacityReductionsV1.NotFound) => Error.NotFound(code,
            "The capacity reduction preview or schedule was not found."),
        (HttpStatusCode.Conflict, CapacityReductionsV1.SourceChanged) => Error.Conflict(code,
            "The covered shops, affiliations or term changed. Review the current subscription before scheduling."),
        (HttpStatusCode.Conflict, CapacityReductionsV1.Expired) => Error.Conflict(code,
            "The capacity reduction preview expired. Generate a fresh preview before confirming."),
        (HttpStatusCode.Conflict, CapacityReductionsV1.MemberConflict) => Error.Conflict(code,
            "One or more retained shops exceed the target active-member allowance. Resolve member counts first."),
        (HttpStatusCode.Conflict, CapacityReductionsV1.AlreadyScheduled) => Error.Conflict(code,
            "A capacity reduction is already scheduled for this subscription term."),
        (HttpStatusCode.Conflict, CapacityReductionsV1.AlreadyFunded) => Error.Conflict(code,
            "This scheduled capacity reduction has already been funded."),
        (HttpStatusCode.Conflict, CapacityReductionsV1.NeedsReview) => Error.Conflict(code,
            "This subscription needs provider review before a reduction can be scheduled."),
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        _ => Unavailable("UpstreamStatus"),
    };
}
