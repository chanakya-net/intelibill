using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string PaymentRoute = OrdersRoute + "/{orderId:guid}/payment";

    [HttpGet(PaymentRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ReadPayment(Guid accountId, Guid orderId,
        [FromServices] IMerchantCheckoutPayments service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.ReadPaymentAsync(caller, accountId, orderId, ct)) : SessionInvalid();

    [HttpPost(PaymentRoute + "/attempts")]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CheckoutPaymentsV1.MaxStartBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> StartPayment(Guid accountId, Guid orderId, StartCheckoutPaymentV1 request,
        [FromServices] IMerchantCheckoutPayments service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await service.StartPaymentAsync(caller, accountId, orderId, request, ct);
        return result.IsError ? Failure(result.FirstError) : StatusCode(
            result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Value.Payment);
    }

    [HttpPost(PaymentRoute + "/attempts/{attemptId:guid}/verify")]
    [RateLimit(Limit = 120, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CheckoutPaymentsV1.MaxVerifyBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> VerifyPayment(Guid accountId, Guid orderId, Guid attemptId, VerifyCheckoutPaymentV1 request,
        [FromServices] IMerchantCheckoutPayments service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.VerifyPaymentAsync(caller, accountId, orderId, attemptId, request, ct)) : SessionInvalid();

    /// <summary>Local simulator relay: absent (404) outside Development/Testing hosts.</summary>
    [HttpPost(PaymentRoute + "/attempts/{attemptId:guid}/simulator/capture")]
    [RequestSizeLimit(CheckoutPaymentsV1.MaxVerifyBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> SimulateCapture(Guid accountId, Guid orderId, Guid attemptId, SimulateCheckoutCaptureV1 request,
        [FromServices] IMerchantCheckoutPayments service, [FromServices] IHostEnvironment environment, CancellationToken ct)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return NotFound();
        return ReadCaller() is { } caller
            ? CheckoutResponse(await service.SimulateCaptureAsync(caller, accountId, orderId, attemptId, request, ct)) : SessionInvalid();
    }
}
