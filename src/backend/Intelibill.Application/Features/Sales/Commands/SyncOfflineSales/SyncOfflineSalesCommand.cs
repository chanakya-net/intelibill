using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed record SyncOfflineSalesCommand(
    Guid ActorUserId,
    Guid ShopId,
    string DeviceId,
    IReadOnlyList<OfflineSaleSyncCommand> Sales);

public sealed record OfflineSaleSyncCommand(
    string ClientSaleId,
    string InvoiceNumber,
    DateTimeOffset SoldAt,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    decimal SubtotalBeforeDiscount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalTaxAmount,
    decimal TotalAmount,
    InstantDiscountType SaleDiscountOverrideType,
    decimal SaleDiscountOverrideValue,
    Guid? ConfiguredSaleRuleId,
    DiscountRuleType? ConfiguredSaleRuleType,
    decimal? ConfiguredSaleRulePercentage,
    decimal? ConfiguredSaleRuleThresholdAmount,
    IReadOnlyList<OfflineSaleSyncLineCommand> Items);

public sealed record OfflineSaleSyncLineCommand(
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
    decimal PreTaxAmountBeforeDiscount,
    decimal ItemDiscountAmount,
    decimal SaleDiscountAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    Guid? ConfiguredBatchRuleId,
    decimal? ConfiguredBatchRulePercentage,
    InstantDiscountType ItemDiscountOverrideType,
    decimal ItemDiscountOverrideValue,
    string? HsnCode);
