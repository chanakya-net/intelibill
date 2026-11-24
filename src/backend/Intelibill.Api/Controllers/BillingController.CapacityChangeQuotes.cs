using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string CapacityQuotesRoute = "{accountId:guid}/subscriptions/{termId:guid}/capacity-change-quotes";

    /// <summary>Preview only: freezes a prorated capacity-increase quote; never charges or changes coverage.</summary>
    [HttpPost(CapacityQuotesRoute)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CapacityChangeQuotesV1.MaxCreateBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CreateCapacityChangeQuote(Guid accountId, Guid termId, CreateCapacityChangeQuoteV1 request,
        [FromServices] IMerchantCapacityChangeQuotes service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.CreateCapacityQuoteAsync(caller, accountId, termId, request, ct)) : SessionInvalid();

    [HttpPost(CapacityQuotesRoute + "/{quoteId:guid}/order")]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CapacityChangeQuotesV1.MaxCreateBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ConvertCapacityQuote(Guid accountId, Guid termId, Guid quoteId,
        ConvertCapacityQuoteV1 request, [FromServices] IMerchantCapacityChangeQuotes service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ConvertCapacityQuoteAsync(caller, accountId, termId, quoteId, request, ct))
            : SessionInvalid();

    [HttpGet(CapacityQuotesRoute + "/{quoteId:guid}/shops")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CapacityChangeQuoteShops(Guid accountId, Guid termId, Guid quoteId, [FromQuery] Guid? after,
        [FromServices] IMerchantCapacityChangeQuotes service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ReadCapacityQuoteShopsAsync(caller, accountId, termId, quoteId, after, ct))
            : SessionInvalid();
}
