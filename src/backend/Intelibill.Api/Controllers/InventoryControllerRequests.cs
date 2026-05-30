using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Api.Controllers;

public sealed record AddInventoryRequest(
    string ItemName,
    string Barcode,
    string? ItemDescription,
    string? HsnCode,
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
