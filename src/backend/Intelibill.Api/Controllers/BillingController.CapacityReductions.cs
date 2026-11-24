using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string CapacityReductionsRoute = "{accountId:guid}/subscriptions/{termId:guid}/capacity-reductions";

    /// <summary>Preview only: returns full renewal price and live aggregate member counts without preselecting shops.</summary>
    [HttpPost(CapacityReductionsRoute + "/previews")]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CapacityReductionsV1.MaxPreviewCreateBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CreateCapacityReductionPreview(Guid accountId, Guid termId,
        CreateCapacityReductionPreviewV1 request, [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.CreatePreviewAsync(caller, accountId, termId, request, ct)) : SessionInvalid();

    [HttpGet(CapacityReductionsRoute + "/previews/{previewId:guid}/shops")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CapacityReductionPreviewShops(Guid accountId, Guid termId, Guid previewId,
        [FromQuery] Guid? after, [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ReadPreviewShopsAsync(caller, accountId, termId, previewId, after, ct))
            : SessionInvalid();

    /// <summary>Schedules an owner-selected reduction for renewal without modifying the current term or shop roster.</summary>
    [HttpPost(CapacityReductionsRoute)]
    [RateLimit(Limit = 20, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CapacityReductionsV1.MaxScheduleBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ScheduleCapacityReduction(Guid accountId, Guid termId,
        ScheduleCapacityReductionV1 request, [FromServices] IMerchantCapacityReductions service, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await service.ScheduleAsync(caller, accountId, termId, request, ct);
        return result.IsError ? Failure(result.FirstError) : StatusCode(
            result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Value.Schedule);
    }

    [HttpGet(CapacityReductionsRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ReadCapacityReduction(Guid accountId, Guid termId,
        [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ReadScheduleAsync(caller, accountId, termId, ct)) : SessionInvalid();

    [HttpGet(CapacityReductionsRoute + "/operations/{operationKey:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCapacityReduction(Guid accountId, Guid termId, Guid operationKey,
        [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.RecoverScheduleAsync(caller, accountId, termId, operationKey, ct))
            : SessionInvalid();

    [HttpGet(CapacityReductionsRoute + "/{scheduleId:guid}/shops")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CapacityReductionShops(Guid accountId, Guid termId, Guid scheduleId,
        [FromQuery] Guid? after, [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ReadScheduleShopsAsync(caller, accountId, termId, scheduleId, after, ct))
            : SessionInvalid();

    [HttpPost(CapacityReductionsRoute + "/{scheduleId:guid}/order")]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CapacityReductionsV1.MaxOrderBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CreateCapacityReductionOrder(Guid accountId, Guid termId, Guid scheduleId,
        CreateReductionOrderV1 request, [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.CreateReductionOrderAsync(caller, accountId, termId, scheduleId, request, ct))
            : SessionInvalid();

    [HttpPost(CapacityReductionsRoute + "/{scheduleId:guid}/resolve")]
    [RateLimit(Limit = 20, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [RequestSizeLimit(CapacityReductionsV1.MaxResolveBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ResolveCapacityReduction(Guid accountId, Guid termId, Guid scheduleId,
        ResolveCapacityReductionV1 request, [FromServices] IMerchantCapacityReductions service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CheckoutResponse(await service.ResolveScheduleAsync(caller, accountId, termId, scheduleId, request, ct))
            : SessionInvalid();
}
