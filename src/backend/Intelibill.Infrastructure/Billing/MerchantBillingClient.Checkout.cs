using System.Globalization;
using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public Task<ErrorOr<MerchantCheckoutQuote>> CreateQuoteAsync(MerchantBillingCaller caller, Guid account,
        CreateCheckoutQuoteV1 request, CancellationToken ct) =>
        RunAsync<MerchantCheckoutQuote>(caller, new(CheckoutOrdersV1.Create, HttpMethod.Post, CheckoutOrdersV1.Path(account),
            account, JsonSerializer.SerializeToUtf8Bytes(request, Json)), CheckoutOrdersV1.MaxQuoteBytes, async (response, token) =>
        {
            var quote = await ReadJsonAsync<CheckoutQuoteV1>(response, token);
            var created = response.StatusCode == HttpStatusCode.Created;
            return quote is not null && IsCheckoutQuoteValid(quote, account) &&
                quote.PlanVersionId == request.PlanVersionId && quote.TermId == request.TermId && quote.Period == request.Period &&
                quote.Revision == request.ExpectedRevision && (!created || quote.Acceptance is null) &&
                (request.TermId is not null || request.ShopIds is { } ids &&
                    quote.ShopCount == ids.Length && quote.ShopHash == CheckoutOrdersV1.ShopHash(ids))
                ? new MerchantCheckoutQuote(quote, created) : Unavailable("InvalidCheckoutQuote");
        }, ct);

    public Task<ErrorOr<CheckoutQuoteV1>> AcceptQuoteAsync(MerchantBillingCaller caller, Guid account, Guid order,
        AcceptCheckoutQuoteV1 request, CancellationToken ct) =>
        RunAsync<CheckoutQuoteV1>(caller, new(CheckoutOrdersV1.Accept, HttpMethod.Post, CheckoutOrdersV1.AcceptPath(account, order),
            account, JsonSerializer.SerializeToUtf8Bytes(request, Json)), CheckoutOrdersV1.MaxQuoteBytes, async (response, token) =>
        {
            var quote = await ReadJsonAsync<CheckoutQuoteV1>(response, token);
            return quote is not null && IsCheckoutQuoteValid(quote, account) && quote.OrderId == order &&
                quote.Acceptance?.OperationKey == request.OperationKey && quote.Revision == request.ExpectedRevision &&
                quote.SnapshotHash == request.SnapshotHash && quote.CustomerTermsVersion == request.CustomerTermsVersion &&
                quote.RefundPolicyVersion == request.RefundPolicyVersion
                ? quote : Unavailable("InvalidCheckoutAcceptance");
        }, ct);

    public Task<ErrorOr<CheckoutQuoteV1>> ReadQuoteAsync(MerchantBillingCaller caller, Guid account, Guid order,
        CancellationToken ct) =>
        CheckoutQuoteAsync(caller, new(CheckoutOrdersV1.Read, HttpMethod.Get, CheckoutOrdersV1.DetailPath(account, order), account, []),
            account, order, null, ct);

    public Task<ErrorOr<CheckoutQuoteV1>> RecoverQuoteAsync(MerchantBillingCaller caller, Guid account, string kind, Guid key,
        CancellationToken ct) => kind is "create" or "accept"
        ? CheckoutQuoteAsync(caller, new(CheckoutOrdersV1.Recover, HttpMethod.Get,
            CheckoutOrdersV1.OperationPath(account, kind, key), account, []), account, null, kind == "accept" ? key : null, ct)
        : Task.FromResult<ErrorOr<CheckoutQuoteV1>>(Error.NotFound(CheckoutOrdersV1.NotFound, "The quote was not found."));

    private Task<ErrorOr<CheckoutQuoteV1>> CheckoutQuoteAsync(MerchantBillingCaller caller, BillingDelegationIntent intent,
        Guid account, Guid? order, Guid? acceptanceKey, CancellationToken ct) =>
        RunAsync<CheckoutQuoteV1>(caller, intent, CheckoutOrdersV1.MaxQuoteBytes, async (response, token) =>
        {
            var quote = await ReadJsonAsync<CheckoutQuoteV1>(response, token);
            return quote is not null && IsCheckoutQuoteValid(quote, account) &&
                (order is null || quote.OrderId == order) && (acceptanceKey is null || quote.Acceptance?.OperationKey == acceptanceKey)
                ? quote : Unavailable("InvalidCheckoutQuote");
        }, ct);

    public Task<ErrorOr<CheckoutShopPageV1>> ReadCheckoutShopsAsync(MerchantBillingCaller caller, Guid account,
        Guid? order, Guid? after, CancellationToken ct) =>
        RunAsync<CheckoutShopPageV1>(caller, order is { } id
            ? new(CheckoutOrdersV1.Shops, HttpMethod.Get, CheckoutOrdersV1.ShopsPath(account, id, after), account, [])
            : new(CheckoutOrdersV1.Candidates, HttpMethod.Get, CheckoutOrdersV1.CandidatesPath(account, after), account, []),
            CheckoutOrdersV1.MaxPageBytes, async (response, token) =>
            {
                var page = await ReadJsonAsync<CheckoutShopPageV1>(response, token);
                return page is not null && IsCheckoutPageValid(page, account, order, after)
                    ? page : Unavailable("InvalidCheckoutShops");
            }, ct);

    internal static bool IsCheckoutQuoteValid(CheckoutQuoteV1 q, Guid account) =>
        q.SchemaVersion == 1 && q.AccountId == account && q.OrderId != Guid.Empty && q.Revision == 1 &&
        q.PlanId != Guid.Empty && q.PlanVersionId != Guid.Empty && q.PlanVersion > 0 &&
        q.PlanName is { Length: > 0 and <= 80 } && q.TermId != Guid.Empty && q.Period is "Month" or "Year" &&
        q.Capacity is > 0 and <= CheckoutOrdersV1.MaxShops && q.ShopCount > 0 && q.ShopCount <= q.Capacity &&
        q.MembersPerShop is > 0 and <= CheckoutOrdersV1.MaxShops &&
        CheckoutOrdersV1.IsHash(q.ShopHash) && CheckoutOrdersV1.IsHash(q.SourceHash) &&
        q.AmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise && q.Currency == "INR" && q.PriceBasis == "TaxInclusiveFinal" &&
        q.CustomerTermsVersion is { Length: > 0 and <= 80 } && q.RefundPolicyVersion is { Length: > 0 and <= 80 } &&
        ((q.Increase is not null) == (q.StartRule == CapacityIncreaseOrderV1.Rule)) && ValidReductionOrder(q) && HasCheckoutDates(q) && ValidFreshStart(q) && q.SnapshotHash == CheckoutOrdersV1.SnapshotHash(q) &&
        (q.Acceptance is null || q.Acceptance is { } a && a.OperationKey != Guid.Empty && a.QuoteRevision == q.Revision &&
            a.SnapshotHash == q.SnapshotHash && EntitlementSnapshotsV1.IsInstant(a.AcceptedAt) &&
            a.AcceptedAt >= q.CreatedAt && a.AcceptedAt < q.ExpiresAt);

    private static bool ValidFreshStart(CheckoutQuoteV1 quote) =>
        quote.FreshStart is null || quote.FreshStart is { Policy: CheckoutOrdersV1.FreshStartPolicy, TrialShopCount: >= 0 } fresh &&
        fresh.TrialShopCount <= quote.ShopCount && CheckoutOrdersV1.IsHash(fresh.TrialSourceHash) &&
        (quote.StartRule == "Fulfilment" || quote.StartRule == "RestoredAccess" && fresh.TrialShopCount == 0);

    private static bool ValidReductionOrder(CheckoutQuoteV1 q) =>
        q.Reduction is null || (q.StartRule == "PriorPaidThrough" && q.Increase is null && q.FreshStart is null &&
            q.Reduction.ScheduleId != Guid.Empty && CheckoutOrdersV1.IsHash(q.Reduction.ScheduleHash) &&
            q.Reduction.SourceShopCount >= q.ShopCount && q.Reduction.SourceShopCount <= CheckoutOrdersV1.MaxShops &&
            CheckoutOrdersV1.IsHash(q.Reduction.SourceShopHash));

    private static bool HasCheckoutDates(CheckoutQuoteV1 q) =>
        q.BillingTimeZone == "Asia/Kolkata" && q.CalendarVersion == "calendar-month-year/1" && q.RenewalPolicyVersion == "renewal/1" &&
        EntitlementSnapshotsV1.IsInstant(q.CreatedAt) && EntitlementSnapshotsV1.IsInstant(q.ExpiresAt) &&
        q.ExpiresAt - q.CreatedAt == TimeSpan.FromMinutes(15) && EntitlementSnapshotsV1.IsInstant(q.PreviewStartsAt) &&
        EntitlementSnapshotsV1.IsInstant(q.PreviewEndsAt) && q.PreviewStartsAt < q.PreviewEndsAt &&
        DateTime.TryParseExact(q.AnchorLocal, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
        (q.StartRule switch
        {
            "Fulfilment" => q.TermId is null && q.Ordinal == 0 && q.GraceDaysIncluded == 0,
            "RestoredAccess" => q.TermId is not null && q.Ordinal == 0 && q.GraceDaysIncluded == 0,
            CapacityIncreaseOrderV1.Rule => q.TermId is not null && q.FreshStart is null && q.Ordinal >= 0 &&
                q.Increase is { RoutingRevision: >= 0, SourceRevision: >= 0 and < long.MaxValue } change && CheckoutOrdersV1.IsHash(change.QuoteHash) &&
                CheckoutOrdersV1.IsHash(change.PinHash) && q.PreviewStartsAt <= q.CreatedAt && q.CreatedAt < q.PreviewEndsAt,
            "PriorPaidThrough" => q.TermId is not null && q.Ordinal is > 0 and <= 1201 && q.GraceDaysIncluded is >= 0 and <= 7,
            _ => false,
        });

    internal static bool IsCheckoutPageValid(CheckoutShopPageV1 page, Guid account, Guid? order, Guid? after)
    {
        if (page.AccountId != account || page.OrderId != order || page.Items is not { Length: <= CheckoutOrdersV1.PageSize } shops)
            return false;
        var prior = after ?? Guid.Empty;
        foreach (var shop in shops)
        {
            if (shop is not { Name.Length: > 0 and <= CheckoutOrdersV1.MaxShopName } ||
                shop.ShopId.CompareTo(prior) <= 0 || shop.TermId == Guid.Empty)
                return false;
            prior = shop.ShopId;
        }
        return page.NextCursor is null || (shops.Length == CheckoutOrdersV1.PageSize && page.NextCursor == prior);
    }

    private Error MapCheckoutFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, CheckoutOrdersV1.Invalid) => Error.Validation(code, "Choose a supported full-payment package and confirm its terms."),
        (HttpStatusCode.NotFound, CheckoutOrdersV1.NotFound) => Error.NotFound(code, "The quote was not found."),
        (HttpStatusCode.Conflict, CheckoutOrdersV1.SourceChanged) => Error.Conflict(code, "The quote revision or covered shops changed."),
        (HttpStatusCode.Conflict, CheckoutOrdersV1.Expired) => Error.Conflict(code, "The quote expired before acceptance."),
        (HttpStatusCode.Conflict, CheckoutOrdersV1.AlreadyAccepted) => Error.Conflict(code, "The quote was already accepted."),
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        (_, CapacityReductionsV1.NotFound or CapacityReductionsV1.SourceChanged or CapacityReductionsV1.MemberConflict
            or CapacityReductionsV1.AlreadyFunded or CapacityReductionsV1.NeedsReview) => MapCapacityReductionFailure(status, code),
        _ => Unavailable("UpstreamStatus"),
    };
}
