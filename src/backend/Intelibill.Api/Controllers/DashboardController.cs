using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController : AuthenticatedControllerBase
{
    public DashboardController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<DashboardDto>>(
            new GetDashboardQuery(UserId!.Value, ActiveShopId!.Value, from, to),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}
