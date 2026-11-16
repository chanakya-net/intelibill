using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string OrdersRoute = "{accountId:guid}/orders";

    [HttpPost(OrdersRoute)]
    [RequestSizeLimit(CheckoutOrdersV1.MaxCreateBytes)]
    public async Task<IActionResult> CreateQuote(Guid accountId, CreateCheckoutQuoteV1 request,
        [FromServices] IMerchantCheckoutOrders service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await service.CreateQuoteAsync(caller, accountId, request, ct);
        return result.IsError ? Failure(result.FirstError) : StatusCode(
            result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Value.Quote);
    }

    [HttpPost(OrdersRoute + "/{orderId:guid}/accept")]
    [RequestSizeLimit(CheckoutOrdersV1.MaxAcceptBytes)]
    public async Task<IActionResult> AcceptQuote(Guid accountId, Guid orderId, AcceptCheckoutQuoteV1 request,
        [FromServices] IMerchantCheckoutOrders service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.AcceptQuoteAsync(caller, accountId, orderId, request, ct)) : SessionInvalid();

    [HttpGet(OrdersRoute + "/{orderId:guid}")]
    public async Task<IActionResult> ReadQuote(Guid accountId, Guid orderId,
        [FromServices] IMerchantCheckoutOrders service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.ReadQuoteAsync(caller, accountId, orderId, ct)) : SessionInvalid();

    [HttpGet(OrdersRoute + "/operations/{kind}/{key:guid}")]
    public async Task<IActionResult> RecoverQuote(Guid accountId, string kind, Guid key,
        [FromServices] IMerchantCheckoutOrders service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.RecoverQuoteAsync(caller, accountId, kind, key, ct)) : SessionInvalid();

    [HttpGet(OrdersRoute + "/eligible-shops")]
    public async Task<IActionResult> CheckoutCandidates(Guid accountId, [FromQuery] Guid? after,
        [FromServices] IMerchantCheckoutOrders service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.ReadCheckoutShopsAsync(caller, accountId, null, after, ct)) : SessionInvalid();

    [HttpGet(OrdersRoute + "/{orderId:guid}/shops")]
    public async Task<IActionResult> CheckoutShops(Guid accountId, Guid orderId, [FromQuery] Guid? after,
        [FromServices] IMerchantCheckoutOrders service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.ReadCheckoutShopsAsync(caller, accountId, orderId, after, ct)) : SessionInvalid();

    private IActionResult CheckoutResponse<T>(ErrorOr<T> result) =>
        result.IsError ? Failure(result.FirstError) : Ok(result.Value);
}
