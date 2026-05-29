namespace Intelibill.Application.Features.Sales.DTOs;

public enum SellableKind
{
    Goods,
    Service,
}

public sealed record SellableDto(
    SellableKind Kind,
    Guid? InventoryBatchId = null,
    string? Barcode = null,
    string? ItemName = null,
    string? BatchNumber = null,
    decimal? Quantity = null,
    decimal? SalesPrice = null,
    decimal? Mrp = null,
    decimal? TaxRatePercent = null,
    bool? TaxIncluded = null,
    bool? PurchaseTaxIncluded = null,
    DateOnly? ExpiryDate = null,
    Guid? ServiceId = null,
    string? Code = null,
    string? Name = null,
    string? Description = null,
    decimal? Price = null,
    string? HsnCode = null)
{
    public static SellableDto FromInventoryBatch(
        Guid inventoryBatchId,
        string barcode,
        string itemName,
        string batchNumber,
        decimal quantity,
        decimal salesPrice,
        decimal mrp,
        decimal taxRatePercent,
        bool taxIncluded,
        bool purchaseTaxIncluded,
        DateOnly? expiryDate) =>
        new(
            SellableKind.Goods,
            InventoryBatchId: inventoryBatchId,
            Barcode: barcode,
            ItemName: itemName,
            BatchNumber: batchNumber,
            Quantity: quantity,
            SalesPrice: salesPrice,
            Mrp: mrp,
            TaxRatePercent: taxRatePercent,
            TaxIncluded: taxIncluded,
            PurchaseTaxIncluded: purchaseTaxIncluded,
            ExpiryDate: expiryDate);

    public static SellableDto FromService(
        Guid serviceId,
        string code,
        string name,
        string? description,
        decimal price,
        string? hsnCode,
        decimal taxRatePercent,
        bool taxIncluded) =>
        new(
            SellableKind.Service,
            ServiceId: serviceId,
            Code: code,
            Name: name,
            Description: description,
            Price: price,
            HsnCode: hsnCode,
            TaxRatePercent: taxRatePercent,
            TaxIncluded: taxIncluded);
}
