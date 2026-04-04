using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/items")]
[Authorize]
public sealed class ItemsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetItems(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<ItemDto>>>(
            new GetItemsQuery(userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddItem([FromBody] AddItemRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<ItemDto>>(
            new AddItemCommand(
                userId.Value,
                activeShopId.Value,
                request.Name,
                request.Barcode,
                request.Description,
                request.Uom,
                request.IsActive,
                request.PreferredSupplierId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private Guid? GetCurrentActiveShopId()
    {
        var activeShopId = User.FindFirst("active_shop_id")?.Value;
        return Guid.TryParse(activeShopId, out var shopId) ? shopId : null;
    }
}

public sealed record AddItemRequest(
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive,
    Guid? PreferredSupplierId);
