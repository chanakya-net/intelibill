using System.IdentityModel.Tokens.Jwt;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/shops")]
[Authorize]
public sealed class ShopsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMyShops(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<IReadOnlyList<UserShopDto>>>(
            new GetMyShopsQuery(userId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    public async Task<IActionResult> CreateShop([FromBody] CreateShopRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new CreateShopCommand(userId.Value, request.Name),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("switch")]
    public async Task<IActionResult> SwitchActiveShop([FromBody] SwitchActiveShopRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new SwitchActiveShopCommand(userId.Value, request.ShopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("default")]
    public async Task<IActionResult> SetDefaultShop([FromBody] SetDefaultShopRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new SetDefaultShopCommand(userId.Value, request.ShopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}

public sealed record CreateShopRequest(string Name);
public sealed record SwitchActiveShopRequest(Guid ShopId);
public sealed record SetDefaultShopRequest(Guid ShopId);