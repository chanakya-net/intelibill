namespace Intelibill.Application.Features.Inventory.DTOs;

public sealed record AvailableBatchDto(
    string Barcode,
    string ItemName,
    string BatchNumber,
    decimal Quantity,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool TaxIncluded,
    DateOnly? ExpiryDate);
