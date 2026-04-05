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
                request.SupplierId,
                request.ReferenceNumber,
                request.Notes,
                request.PerformedAt),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("inbound/batch")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddInventoryBatch([FromBody] AddInventoryBatchRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        if (request.Items.Count == 0)
            return new List<Error>
            {
                Error.Validation("Inventory.BatchEmpty", "At least one inventory row is required.")
            }.ToProblemResult();

        if (request.Items.Count > 100)
            return new List<Error>
            {
                Error.Validation("Inventory.BatchLimitExceeded", "A maximum of 100 inventory rows can be saved at once.")
            }.ToProblemResult();

        var succeeded = new List<AddInventoryBatchSucceededRow>();
        var failed = new List<AddInventoryBatchFailedRow>();

        foreach (var row in request.Items)
        {
            var result = await bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(
                new AddInventoryCommand(
                    userId.Value,
                    activeShopId.Value,
                    row.ItemName,
                    row.Barcode,
                    row.ItemDescription,
                    row.Uom,
                    row.BatchNumber,
                    row.Quantity,
                    row.CostPrice,
                    row.Mrp,
                    row.SalesPrice,
                    row.MinSalePrice,
                    row.TaxRatePercent,
                    row.ExpiryDate,
                    row.ManufacturingDate,
                    row.SupplierId,
                    row.ReferenceNumber,
                    row.Notes,
                    row.PerformedAt),
                cancellationToken);

            if (result.IsError)
            {
                failed.Add(
                    new AddInventoryBatchFailedRow(
                        row.ClientRowId,
                        row.ItemName,
                        row.Barcode,
                        result.Errors.Select(error => new AddInventoryBatchRowError(error.Code, error.Description)).ToArray()));
                continue;
            }

            succeeded.Add(new AddInventoryBatchSucceededRow(row.ClientRowId, result.Value));
        }

        return Ok(
            new AddInventoryBatchResponse(
                request.Items.Count,
                succeeded.Count,
                failed.Count,
                succeeded,
                failed));
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
    Guid? SupplierId,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? PerformedAt);

public sealed record AddInventoryBatchRequest(IReadOnlyList<AddInventoryBatchRowRequest> Items);

public sealed record AddInventoryBatchRowRequest(
    string ClientRowId,
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
    Guid? SupplierId,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? PerformedAt);

public sealed record AddInventoryBatchResponse(
    int RequestedCount,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<AddInventoryBatchSucceededRow> Succeeded,
    IReadOnlyList<AddInventoryBatchFailedRow> Failed);

public sealed record AddInventoryBatchSucceededRow(string ClientRowId, AddInventoryResultDto Result);

public sealed record AddInventoryBatchFailedRow(
    string ClientRowId,
    string ItemName,
    string Barcode,
    IReadOnlyList<AddInventoryBatchRowError> Errors);

public sealed record AddInventoryBatchRowError(string Code, string Description);
