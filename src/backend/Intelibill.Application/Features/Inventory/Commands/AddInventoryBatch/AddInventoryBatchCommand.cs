using Intelibill.Application.Features.Inventory.DTOs;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

public sealed record AddInventoryBatchCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    IReadOnlyList<AddInventoryBatchRowCommand> Items);

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

public sealed record AddInventoryBatchFailedRowDto(
    string ClientRowId,
    string ItemName,
    string Barcode,
    IReadOnlyList<AddInventoryBatchRowErrorDto> Errors);

public sealed record AddInventoryBatchRowErrorDto(string Code, string Description);
