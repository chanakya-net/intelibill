using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;
using Intelibill.Application.Features.Inventory.Queries.GetAdjustmentHistory;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed partial class InventoryController : AuthenticatedControllerBase
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
                request.TotalPurchaseCost,
                request.Mrp,
                request.SalesPrice,
                request.TaxRatePercent,
                request.TaxIncluded,
                request.PurchaseTaxIncluded,
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
                        row.TotalPurchaseCost,
                        row.Mrp,
                        row.SalesPrice,
                        row.TaxRatePercent,
                        row.TaxIncluded,
                        row.PurchaseTaxIncluded,
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
}
