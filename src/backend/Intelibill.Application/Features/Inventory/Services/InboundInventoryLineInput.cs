using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Inventory.Services;

public sealed record InboundInventoryLineInput(
    Guid? ItemId,
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

public sealed record InboundInventoryLineResult(
    Item Item,
    InventoryBatch Batch,
    StockTransaction StockTransaction,
    SupplierLedgerEntry LedgerEntry,
    Domain.Entities.Inventory Inventory);
