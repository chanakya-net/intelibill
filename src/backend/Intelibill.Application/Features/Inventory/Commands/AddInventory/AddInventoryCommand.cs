namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

public sealed record AddInventoryCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
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
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset? PerformedAt);