using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed record RecordSaleCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    IReadOnlyList<RecordSaleItemCommand> Items);

public sealed record RecordSaleItemCommand(
    string Barcode,
    string BatchNumber,
    string ItemName,
    decimal Quantity,
    decimal CostPrice,
    decimal SalesPrice,
    decimal Mrp,
    decimal TaxRatePercent,
    bool IsPriceIncludingTax,
    Guid InventoryBatchId,
    string? ClientLineKey = null);
