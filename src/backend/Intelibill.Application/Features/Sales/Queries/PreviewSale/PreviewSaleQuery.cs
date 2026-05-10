using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Queries.PreviewSale;

public sealed record PreviewSaleQuery(
    Guid ActorUserId,
    Guid ShopId,
    InstantDiscount SaleDiscount,
    IReadOnlyList<PreviewSaleLineQuery> Items);

public sealed record PreviewSaleLineQuery(
    Guid InventoryBatchId,
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    InstantDiscount ItemDiscount,
    string? ClientLineKey = null);

