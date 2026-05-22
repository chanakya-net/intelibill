using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal static class OfflineSaleSyncIdempotencyHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict,
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
            (int)sale.PaymentMethod,
            sale.PaidAmount,
            sale.DueAmount,
            sale.SubtotalBeforeDiscount,
            sale.TotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.TotalTaxAmount,
            sale.TotalAmount,
            (int)sale.SaleDiscountOverrideType,
            sale.SaleDiscountOverrideValue,
            sale.ConfiguredSaleRuleId,
            sale.ConfiguredSaleRuleType is null ? null : (int)sale.ConfiguredSaleRuleType,
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
                (int)item.ItemDiscountOverrideType,
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
    int PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    decimal SubtotalBeforeDiscount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalTaxAmount,
    decimal TotalAmount,
    int SaleDiscountOverrideType,
    decimal SaleDiscountOverrideValue,
    Guid? ConfiguredSaleRuleId,
    int? ConfiguredSaleRuleType,
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
    int ItemDiscountOverrideType,
    decimal ItemDiscountOverrideValue,
    string? HsnCode);
