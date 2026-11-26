using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpGet(OrdersRoute + "/{orderId:guid}/billing-documents")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ListBillingDocuments(Guid accountId, Guid orderId,
        [FromServices] IMerchantBillingDocuments service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        Response.Headers.CacheControl = "no-store";
        return CheckoutResponse(await service.ListDocumentsAsync(caller, accountId, orderId, ct));
    }

    [HttpGet(OrdersRoute + "/{orderId:guid}/billing-documents/before/{beforeVersion:int}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ListBillingDocumentsBefore(Guid accountId, Guid orderId, int beforeVersion,
        [FromServices] IMerchantBillingDocuments service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        Response.Headers.CacheControl = "no-store";
        return CheckoutResponse(await service.ListDocumentsAsync(caller, accountId, orderId, ct, beforeVersion));
    }

    [HttpGet(OrdersRoute + "/{orderId:guid}/billing-documents/{versionId:guid}/content")]
    [RateLimit(Limit = 60, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> DownloadBillingDocument(Guid accountId, Guid orderId, Guid versionId,
        [FromServices] IMerchantBillingDocuments service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await service.DownloadDocumentAsync(caller, accountId, orderId, versionId, ct);
        if (result.IsError) return Failure(result.FirstError);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(result.Value.Bytes, BillingDocumentsV1.MediaType, result.Value.FileName);
    }
}
