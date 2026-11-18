using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string CancellationRoute = "{accountId:guid}/subscriptions/{termId:guid}/cancellation";

    [HttpGet(CancellationRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ReadCancellation(Guid accountId, Guid termId, [FromQuery] Guid? after,
        [FromServices] IMerchantSubscriptionCancellations service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CancellationResponse(await service.ReadCancellationAsync(caller, accountId, termId, after, ct))
            : SessionInvalid();

    [HttpPost(CancellationRoute)]
    [RequestSizeLimit(SubscriptionCancellationsV1.MaximumBodyBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ScheduleCancellation(Guid accountId, Guid termId,
        [FromBody] ScheduleSubscriptionCancellationV1 request, [FromServices] IMerchantSubscriptionCancellations service,
        CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CancellationResponse(await service.ScheduleCancellationAsync(caller, accountId, termId, request, ct))
            : SessionInvalid();

    [HttpGet(CancellationRoute + "/operations/{operation:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCancellation(Guid accountId, Guid termId, Guid operation,
        [FromServices] IMerchantSubscriptionCancellations service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CancellationResponse(await service.RecoverCancellationAsync(caller, accountId, termId, operation, ct))
            : SessionInvalid();

    [HttpPost(CancellationRoute + "/withdrawal")]
    [RequestSizeLimit(SubscriptionCancellationsV1.MaximumBodyBytes)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> WithdrawCancellation(Guid accountId, Guid termId,
        [FromBody] WithdrawSubscriptionCancellationV1 request, [FromServices] IMerchantSubscriptionCancellations service,
        CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CancellationResponse(await service.WithdrawCancellationAsync(caller, accountId, termId, request, ct))
            : SessionInvalid();

    [HttpGet(CancellationRoute + "/withdrawal/operations/{operation:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCancellationWithdrawal(Guid accountId, Guid termId, Guid operation,
        [FromServices] IMerchantSubscriptionCancellations service, CancellationToken ct) =>
        ReadCaller() is { } caller
            ? CancellationResponse(await service.RecoverCancellationWithdrawalAsync(caller, accountId, termId, operation, ct))
            : SessionInvalid();

    private IActionResult CancellationResponse<T>(ErrorOr<T> result) =>
        result.IsError ? Failure(result.FirstError) : Ok(result.Value);
}
