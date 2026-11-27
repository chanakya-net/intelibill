using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/financial-operations")]
[Authorize]
public sealed class FinancialOperationsController : AuthenticatedControllerBase
{
    public FinancialOperationsController(IMessageBus bus) : base(bus)
    {
    }

    /// <summary>
    /// The committed outcome of a keyed financial operation, or 404 when none is visible yet. A 404
    /// never means a timed-out request failed; resend it with the same key to converge.
    /// </summary>
    [HttpGet("{operationType}/{operationKey}")]
    [Authorize(Policy = "OwnerOrManager")]
    [LicenceAction(LicenceAction.Recovery)]
    public async Task<IActionResult> GetFinancialOperation(string operationType, string operationKey, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<FinancialOperationDto>>(
            new GetFinancialOperationQuery(UserId!.Value, ActiveShopId!.Value, operationType, operationKey, SessionClaims.TryRead(User)),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}
