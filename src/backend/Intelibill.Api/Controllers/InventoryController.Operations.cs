using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;
using Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;
using Intelibill.Application.Features.Inventory.Commands.VoidBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

public sealed partial class InventoryController
{
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
