using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string TrialRoute = "/api/licensing/shops/{shopId:guid}/trial";

    [HttpGet(TrialRoute)]
    public Task<IActionResult> PreviewTrial(Guid shopId, [FromServices] IMerchantShopTrials trials, CancellationToken ct) =>
        WithTrialCaller(caller => trials.PreviewTrialAsync(caller, shopId, ct));

    [HttpPost(TrialRoute)]
    [RequestSizeLimit(ShopTrialsV1.MaximumBodyBytes)]
    public Task<IActionResult> AcceptTrial(Guid shopId, [FromBody] ShopTrialAcceptanceV1? request,
        [FromServices] IMerchantShopTrials trials, CancellationToken ct) =>
        WithTrialCaller(caller => trials.AcceptTrialAsync(caller, shopId, request, ct));

    [HttpGet(TrialRoute + "/operations/{operation:guid}")]
    public Task<IActionResult> ReadTrial(Guid shopId, Guid operation, [FromServices] IMerchantShopTrials trials,
        CancellationToken ct) => WithTrialCaller(caller => trials.ReadTrialAsync(caller, shopId, operation, ct));

    private async Task<IActionResult> WithTrialCaller<T>(Func<MerchantBillingCaller, Task<ErrorOr<T>>> action)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await action(caller);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
