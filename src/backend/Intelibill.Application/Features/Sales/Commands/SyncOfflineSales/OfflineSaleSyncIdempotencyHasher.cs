using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal static class OfflineSaleSyncIdempotencyHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string ComputeKey(string deviceId, string clientSaleId)
    {
        var normalized = $"{Normalize(deviceId)}|{Normalize(clientSaleId)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"offline-{Convert.ToHexString(hash)}";
    }

    internal static string ComputeHash(
        Guid shopId,
        string deviceId,
        OfflineSaleSyncCommand sale)
    {
        var payload = new OfflineSaleSyncIdempotencyPayload(
            shopId,
            Normalize(deviceId),
            Normalize(sale.ClientSaleId),
            Normalize(sale.InvoiceNumber),
            sale.SoldAt,
            sale.CustomerId,
            Normalize(sale.CustomerName),
            Normalize(sale.CustomerPhone),
            sale.PaymentMethod,
            sale.PaidAmount,
            sale.DueAmount,
            sale.SubtotalBeforeDiscount,
            sale.TotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.TotalTaxAmount,
            sale.TotalAmount,
            sale.SaleDiscountOverrideType,
            sale.SaleDiscountOverrideValue,
            sale.ConfiguredSaleRuleId,
            sale.ConfiguredSaleRuleType,
            sale.ConfiguredSaleRulePercentage,
            sale.ConfiguredSaleRuleThresholdAmount,
            sale.Items.Select(item => new OfflineSaleSyncLineIdempotencyPayload(
                Normalize(item.Barcode),
                Normalize(item.BatchNumber),
                Normalize(item.ItemName),
                item.Quantity,
                item.CostPrice,
                item.SalesPrice,
                item.Mrp,
                item.TaxRatePercent,
                item.IsPriceIncludingTax,
                item.InventoryBatchId,
                item.PreTaxAmountBeforeDiscount,
                item.ItemDiscountAmount,
                item.SaleDiscountAmount,
                item.TaxableAmount,
                item.TaxAmount,
                item.TotalAmount,
                item.ConfiguredBatchRuleId,
                item.ConfiguredBatchRulePercentage,
                item.ItemDiscountOverrideType,
                item.ItemDiscountOverrideValue,
                Normalize(item.HsnCode))).ToList());

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record OfflineSaleSyncIdempotencyPayload(
    Guid ShopId,
    string? DeviceId,
    string? ClientSaleId,
    string? InvoiceNumber,
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
    IReadOnlyList<OfflineSaleSyncLineIdempotencyPayload> Items);

internal sealed record OfflineSaleSyncLineIdempotencyPayload(
    string? Barcode,
    string? BatchNumber,
    string? ItemName,
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
