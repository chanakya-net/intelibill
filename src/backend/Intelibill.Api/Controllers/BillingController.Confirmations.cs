using Intelibill.Application.Common.Licensing;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpGet(PaymentRoute + "/confirmations")]
    [HttpGet(PaymentRoute + "/confirmations/after/{after:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ListConfirmations(Guid accountId, Guid orderId, Guid? after,
        [FromServices] IMerchantPaymentConfirmations service, CancellationToken ct) =>
        ReadCaller() is { } caller ? CheckoutResponse(await service.ListConfirmationsAsync(caller, accountId, orderId, after, ct)) : SessionInvalid();

    [HttpGet(PaymentRoute + "/confirmations/{id:guid}/document")]
    [RateLimit(Limit = 120, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> DownloadConfirmation(Guid accountId, Guid orderId, Guid id,
        [FromServices] IMerchantPaymentConfirmations service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await service.DownloadConfirmationAsync(caller, accountId, orderId, id, ct);
        if (result.IsError) return Failure(result.FirstError);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value.Bytes, PaymentConfirmationsV1.MediaType, result.Value.FileName);
    }
}
