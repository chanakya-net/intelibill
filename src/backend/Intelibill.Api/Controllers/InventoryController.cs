using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryController(IMessageBus bus) : ControllerBase
{
    [HttpPost("inbound")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddInventory([FromBody] AddInventoryRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(
            new AddInventoryCommand(
                userId.Value,
                activeShopId.Value,
                request.ItemName,
                request.Barcode,
                request.ItemDescription,
                request.Uom,
                request.BatchNumber,
                request.Quantity,
                request.CostPrice,
                request.Mrp,
                request.SalesPrice,
                request.MinSalePrice,
                request.TaxRatePercent,
                request.ExpiryDate,
                request.ManufacturingDate,
                request.ReferenceNumber,
                request.Notes,
                request.PerformedAt),
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

public sealed record AddInventoryRequest(
    string ItemName,
    string Barcode,
    string? ItemDescription,
    string Uom,
    string BatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal MinSalePrice,
    decimal TaxRatePercent,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? PerformedAt);
