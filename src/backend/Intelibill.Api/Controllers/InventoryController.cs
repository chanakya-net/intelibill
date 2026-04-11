using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Application.Features.Inventory.Commands.VoidBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryController(IMessageBus bus) : ControllerBase
{
    [HttpGet("batches")]
    public async Task<IActionResult> GetInventoryBatches(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<InventoryBatchDto>>>(
            new GetInventoryBatchesQuery(userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

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
                request.TaxRatePercent,
                request.TaxIncluded,
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
                Error.Validation("Inventory.BatchLimitExceeded", "Only 100 items are allowed in a batch.")
            }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<AddInventoryBatchResultDto>>(
            new AddInventoryBatchCommand(
                userId.Value,
                activeShopId.Value,
                request.Items.Select(
                    row => new AddInventoryBatchRowCommand(
                        row.ClientRowId,
                        row.ItemName,
                        row.Barcode,
                        row.ItemDescription,
                        row.Uom,
                        row.BatchNumber,
                        row.Quantity,
                        row.CostPrice,
                        row.Mrp,
                        row.SalesPrice,
                        row.TaxRatePercent,
                        row.TaxIncluded,
                        row.ExpiryDate,
                        row.ManufacturingDate,
                        row.SupplierId,
                        row.ReferenceNumber,
                        row.Notes,
                        row.PerformedAt))
                    .ToArray()),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        var response = new AddInventoryBatchResponse(
            result.Value.RequestedCount,
            result.Value.SuccessCount,
            result.Value.FailedCount,
            result.Value.Succeeded
                .Select(row => new AddInventoryBatchSucceededRow(row.ClientRowId, row.Result))
                .ToArray(),
            result.Value.Failed
                .Select(
                    row => new AddInventoryBatchFailedRow(
                        row.ClientRowId,
                        row.ItemName,
                        row.Barcode,
                        row.Errors.Select(error => new AddInventoryBatchRowError(error.Code, error.Description)).ToArray()))
                .ToArray());

        if (result.Value.FailedCount > 0)
        {
            return new ObjectResult(response) { StatusCode = 207 };
        }

        return Ok(response);
    }

    [HttpPost("batches/{batchId:guid}/void")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> VoidBatch(Guid batchId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<VoidBatchResultDto>>(
            new VoidBatchCommand(batchId, userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPut("batches/{batchId:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> UpdateInventoryBatch(Guid batchId, [FromBody] UpdateInventoryBatchRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<Success>>(
            new UpdateInventoryBatchCommand(
                batchId,
                userId.Value,
                activeShopId.Value,
                request.NewBatchNumber,
                request.Quantity,
                request.CostPrice,
                request.Mrp,
                request.SalesPrice,
                request.TaxRatePercent,
                request.TaxIncluded,
                request.ExpiryDate,
                request.ManufacturingDate,
                request.SupplierId,
                request.Notes,
                request.EntryDate),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return Ok();
    }

    [HttpPost("batches/{batchId:guid}/reassign-supplier")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> ReassignBatchSupplier(Guid batchId, [FromBody] ReassignBatchSupplierRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<Success>>(
            new ReassignBatchSupplierCommand(userId.Value, activeShopId.Value, batchId, request.NewSupplierId),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return Ok();
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
    decimal TaxRatePercent,
    bool TaxIncluded,
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
    decimal TaxRatePercent,
    bool TaxIncluded,
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

public sealed record UpdateInventoryBatchRequest(
    string? NewBatchNumber,
    decimal Quantity,
    decimal CostPrice,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    Guid? SupplierId,
    string? Notes,
    DateOnly? EntryDate);

public sealed record ReassignBatchSupplierRequest(Guid NewSupplierId);

public sealed record AddInventoryBatchSucceededRow(string ClientRowId, AddInventoryResultDto Result);

public sealed record AddInventoryBatchFailedRow(
    string ClientRowId,
    string ItemName,
    string Barcode,
    IReadOnlyList<AddInventoryBatchRowError> Errors);

public sealed record AddInventoryBatchRowError(string Code, string Description);
