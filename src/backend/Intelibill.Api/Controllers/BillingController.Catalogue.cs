using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    [HttpGet("/api/billing/plans")]
    public async Task<IActionResult> ListPlans([FromQuery] Guid? after, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await billing.ListPlansAsync(caller, after, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("/api/billing/plans/versions/{versionId:guid}")]
    public async Task<IActionResult> ReadPlanVersion(Guid versionId, CancellationToken cancellationToken)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();
        var result = await billing.ReadPlanVersionAsync(caller, versionId, cancellationToken);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
