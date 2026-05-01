using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var activeShopIdValue = User.FindFirst("active_shop_id")?.Value;
        if (!Guid.TryParse(activeShopIdValue, out var shopId))
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var resolvedEndDate = endDate ?? today;
        var resolvedStartDate = startDate ?? resolvedEndDate.AddDays(-29);

        var result = await bus.InvokeAsync<ErrorOr<DashboardDto>>(
            new GetDashboardQuery(userId, shopId, resolvedStartDate, resolvedEndDate),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}
