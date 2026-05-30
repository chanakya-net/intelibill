namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

public sealed record AddInventoryCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
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
