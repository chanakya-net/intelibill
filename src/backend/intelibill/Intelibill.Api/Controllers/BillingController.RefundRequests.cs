using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string RefundRequestsRoute = "{accountId:guid}/refund-requests";

    /// <summary>Records one refund request against a verified payment confirmation; never approves or alters access.</summary>
    [HttpPost(RefundRequestsRoute)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(RefundRequestsV1.MaxCreateBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CreateRefundRequest(Guid accountId, [ValidateNever] CreateRefundRequestV1 request,
        [FromServices] IMerchantRefundRequests service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.CreateRefundRequestAsync(caller, accountId, request, ct))
            : SessionInvalid();

    [HttpGet(RefundRequestsRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ListRefundRequests(Guid accountId, [FromQuery] Guid? after,
        [FromServices] IMerchantRefundRequests service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ListRefundRequestsAsync(caller, accountId, after, ct))
            : SessionInvalid();
}
