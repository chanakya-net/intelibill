using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;
using Intelibill.Application.Features.Inventory.Commands.VoidBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;
using Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;
using Intelibill.Application.Features.Inventory.Queries.GetAdjustmentHistory;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryController : AuthenticatedControllerBase
{
    public InventoryController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet("batches")]
    public async Task<IActionResult> GetInventoryBatches(CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<InventoryBatchDto>>>(
            new GetInventoryBatchesQuery(UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("adjustments")]
    public async Task<IActionResult> GetAdjustmentHistory(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? itemId = null,
        [FromQuery] Guid? batchId = null,
        [FromQuery] InventoryAdjustmentDirection? direction = null,
        [FromQuery] InventoryAdjustmentReason? reason = null,
        [FromQuery(Name = "from")] DateTimeOffset? from = null,
        [FromQuery(Name = "to")] DateTimeOffset? to = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PaginatedList<InventoryAdjustmentHistoryDto>>>(
            new GetAdjustmentHistoryQuery(
                UserId!.Value,
                ActiveShopId!.Value,
                pageNumber,
                pageSize,
                itemId,
                batchId,
                direction,
                reason,
                from,
                to,
                includeVoided),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("inbound")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddInventory([FromBody] AddInventoryRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(
            new AddInventoryCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.ItemName,
                request.Barcode,
                request.ItemDescription,
                request.HsnCode,
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
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

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

        var result = await Bus.InvokeAsync<ErrorOr<AddInventoryBatchResultDto>>(
            new AddInventoryBatchCommand(
                UserId!.Value,
                ActiveShopId!.Value,
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
                        row.PerformedAt,
                        row.HsnCode))
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
            return new ObjectResult(response) { StatusCode = 207 };

        return Ok(response);
    }

    [HttpPost("batches/{batchId:guid}/void")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> VoidBatch(Guid batchId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<VoidBatchResultDto>>(
            new VoidBatchCommand(batchId, UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("adjustments/{adjustmentId:guid}/void")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> VoidAdjustment(
        Guid adjustmentId,
        [FromBody] VoidAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<VoidAdjustmentResultDto>>(
            new VoidAdjustmentCommand(adjustmentId, UserId!.Value, ActiveShopId!.Value, request.Reason),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("batches/{batchId:guid}/adjust")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> CreateAdjustment(Guid batchId, [FromBody] CreateAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<InventoryAdjustmentResultDto>>(
            new CreateAdjustmentCommand(
                batchId,
                UserId!.Value,
                ActiveShopId!.Value,
                request.Direction,
                request.Reason,
                request.Quantity,
                request.PerformedAt,
                request.Notes),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPut("batches/{batchId:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> UpdateInventoryBatch(Guid batchId, [FromBody] UpdateInventoryBatchRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new UpdateInventoryBatchCommand(
                batchId,
                UserId!.Value,
                ActiveShopId!.Value,
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
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new ReassignBatchSupplierCommand(UserId!.Value, ActiveShopId!.Value, batchId, request.NewSupplierId),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return Ok();
    }

    [HttpGet("batches/available")]
    public async Task<IActionResult> GetAvailableBatches([FromQuery] string? searchTerm, [FromQuery] string? barcode, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var isBarcodeLookup = string.IsNullOrWhiteSpace(searchTerm) && !string.IsNullOrWhiteSpace(barcode);
        var effectiveSearchTerm = isBarcodeLookup ? barcode : searchTerm;

        if (string.IsNullOrWhiteSpace(effectiveSearchTerm))
            return new List<Error>
            {
                Errors.Inventory.SearchTermRequired
            }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(
            new GetAvailableBatchesQuery(UserId!.Value, ActiveShopId!.Value, effectiveSearchTerm, isBarcodeLookup),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record AddInventoryRequest(
    string ItemName,
    string Barcode,
    string? ItemDescription,
    string? HsnCode,
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
    DateTimeOffset? PerformedAt,
    string? HsnCode);

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

public sealed record CreateAdjustmentRequest(
    InventoryAdjustmentDirection Direction,
    InventoryAdjustmentReason Reason,
    decimal Quantity,
    DateTimeOffset? PerformedAt,
    string? Notes);

public sealed record VoidAdjustmentRequest(string Reason);

public sealed record AddInventoryBatchSucceededRow(string ClientRowId, AddInventoryResultDto Result);

public sealed record AddInventoryBatchFailedRow(
    string ClientRowId,
    string ItemName,
    string Barcode,
    IReadOnlyList<AddInventoryBatchRowError> Errors);

public sealed record AddInventoryBatchRowError(string Code, string Description);
