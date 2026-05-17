using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Hsn.Queries.LookupHsn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/hsn")]
[Authorize]
public sealed class HsnController : AuthenticatedControllerBase
{
    public HsnController(IMessageBus bus) : base(bus)
    {
    }

    [HttpPost("lookup")]
    public async Task<IActionResult> LookupHsn([FromBody] LookupHsnRequest request, CancellationToken cancellationToken)
    {
        var authCheck = CheckAuthAndShop();
        if (authCheck is not null) return authCheck;

        var result = await Bus.InvokeAsync<ErrorOr<LookupHsnResult>>(
            new LookupHsnQuery(request.ProductName, UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.Match(Ok, errors => errors.ToProblemResult());
    }
}

public sealed record LookupHsnRequest(string ProductName);
