using Intelibill.Application.Features.Inventory.DTOs;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

/// <param name="OperationKey">
/// Optional durable identity of this submission. Resending the same rows under the same key
/// replays committed rows and retries the rest; changed rows under the same key are refused.
/// </param>
public sealed record AddInventoryBatchCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    IReadOnlyList<AddInventoryBatchRowCommand> Items,
    string? OperationKey = null);

public sealed record AddInventoryBatchRowCommand(
    string ClientRowId,
    string ItemName,
    string Barcode,
    string? ItemDescription,
    string Uom,
    string BatchNumber,
    decimal Quantity,
    decimal TotalPurchaseCost,
    decimal Mrp,
    decimal SalesPrice,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool PurchaseTaxIncluded,
    DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate,
    Guid? SupplierId,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? PerformedAt,
    string? HsnCode);

public sealed record AddInventoryBatchResultDto(
    int RequestedCount,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<AddInventoryBatchSucceededRowDto> Succeeded,
    IReadOnlyList<AddInventoryBatchFailedRowDto> Failed);

public sealed record AddInventoryBatchSucceededRowDto(string ClientRowId, AddInventoryResultDto Result);

/// <param name="Retryable">
/// True when the row failed for a temporary reason and nothing was saved: send the same batch
/// again. False when the row itself was rejected.
/// </param>
public sealed record AddInventoryBatchFailedRowDto(
    string ClientRowId,
    string ItemName,
    string Barcode,
    IReadOnlyList<AddInventoryBatchRowErrorDto> Errors,
    bool Retryable = false);

public sealed record AddInventoryBatchRowErrorDto(string Code, string Description);
