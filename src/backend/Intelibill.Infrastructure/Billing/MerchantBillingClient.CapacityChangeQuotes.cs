using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCapacityChangeQuotes
{
    public Task<ErrorOr<CapacityChangeQuoteV1>> CreateCapacityQuoteAsync(MerchantBillingCaller caller, Guid account, Guid term,
        CreateCapacityChangeQuoteV1 request, CancellationToken ct) =>
        RunAsync<CapacityChangeQuoteV1>(caller, new(CapacityChangeQuotesV1.Create, HttpMethod.Post,
            CapacityChangeQuotesV1.Path(account, term), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            CapacityChangeQuotesV1.MaxQuoteBytes, async (response, token) =>
            {
                var quote = await ReadJsonAsync<CapacityChangeQuoteV1>(response, token);
                return quote is not null && IsCapacityQuoteValid(quote, account, term) &&
                    quote.TargetPlanVersionId == request.TargetPlanVersionId && quote.TargetCapacity == request.TargetCapacity
                    ? quote : Unavailable("InvalidCapacityChangeQuote");
            }, ct);

    public Task<ErrorOr<CapacityChangeShopPageV1>> ReadCapacityQuoteShopsAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid quote, Guid? after, CancellationToken ct) =>
        RunAsync<CapacityChangeShopPageV1>(caller, new(CapacityChangeQuotesV1.Shops, HttpMethod.Get,
            CapacityChangeQuotesV1.ShopsPath(account, term, quote, after), account, []), CapacityChangeQuotesV1.MaxPageBytes,
            async (response, token) =>
            {
                var page = await ReadJsonAsync<CapacityChangeShopPageV1>(response, token);
                return page is not null && page.QuoteId == quote && IsCheckoutPageValid(
                    new(page.AccountId, null, [.. (page.Items ?? []).Select(s => new CheckoutShopV1(s.ShopId, s.Name, null))],
                        page.NextCursor), account, null, after)
                    ? page : Unavailable("InvalidCapacityChangeShops");
            }, ct);

    /// <summary>
    /// Structural trust boundary for the relayed quote: it must be this account and term, internally coherent and
    /// carry the digest of its own facts. Only a payable quote carries an amount.
    /// </summary>
    internal static bool IsCapacityQuoteValid(CapacityChangeQuoteV1 q, Guid account, Guid term) =>
        q.SchemaVersion == 1 && q.AccountId == account && q.TermId == term && q.QuoteId != Guid.Empty &&
        CapacityChangeQuotesV1.Outcomes.Contains(q.Outcome) && q.Actions is { Length: <= 4 } &&
        q.TargetPlanName is { Length: > 0 and <= 80 } && q.Period is "Month" or "Year" &&
        q.PriorCapacity is > 0 and <= CheckoutOrdersV1.MaxShops && q.TargetCapacity >= q.PriorCapacity &&
        q.TargetCapacity <= CheckoutOrdersV1.MaxShops && q.ShopCount > 0 && q.ShopCount <= q.PriorCapacity &&
        q.TargetMemberAllowance >= q.PriorMemberAllowance && CheckoutOrdersV1.IsHash(q.ShopHash) &&
        CheckoutOrdersV1.IsHash(q.SourceHash) && q.Currency == "INR" && q.TargetGrossPaise is > 0 and <= CheckoutOrdersV1.MaxPaise &&
        (q.Outcome == "Payable") == (q.AmountPaise is > 0) && (q.AmountPaise is null || q.AmountPaise <= q.DifferencePaise) &&
        q.IntervalStartsAt < q.PaidThrough && q.ExpiresAt - q.CreatedAt == TimeSpan.FromMinutes(15) &&
        q.ValidUntil <= q.ExpiresAt && q.BillingTimeZone == "Asia/Kolkata" && q.ProrationVersion == "proration/1" &&
        q.SnapshotHash == CapacityChangeQuotesV1.SnapshotHash(q);

    private Error MapCapacityQuoteFailure(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.BadRequest, CapacityChangeQuotesV1.Invalid) => Error.Validation(code,
            "Choose a published package that increases capacity without reducing the member allowance."),
        (HttpStatusCode.NotFound, CapacityChangeQuotesV1.NotFound) => Error.NotFound(code, "The subscription term or quote was not found."),
        (HttpStatusCode.Conflict, CapacityChangeQuotesV1.SourceChanged) => Error.Conflict(code,
            "The subscription term changed. Review its current capacity and paid-through date."),
        (HttpStatusCode.Conflict, CapacityChangeQuotesV1.NeedsReview) => Error.Conflict(code,
            "This subscription needs provider review before a change can be quoted."),
        (HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch") => MerchantBillingErrors.PayloadMismatch,
        _ => Unavailable("UpstreamStatus"),
    };
}
