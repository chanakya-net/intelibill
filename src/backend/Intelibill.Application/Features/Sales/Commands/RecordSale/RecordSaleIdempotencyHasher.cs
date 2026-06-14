using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

internal static class RecordSaleIdempotencyHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string ComputeHash(RecordSaleCommand command)
    {
        var saleDiscount = command.SaleDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m);
        var payload = new RecordSaleIdempotencyPayload(
            command.ShopId,
            command.ActorUserId,
            command.CustomerId,
            Normalize(command.CustomerName),
            Normalize(command.CustomerPhone),
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            saleDiscount.Type,
            saleDiscount.Value,
            command.CreditNoteAppliedAmount,
            command.CreditNoteRedemptions is { Count: > 0 }
                ? command.CreditNoteRedemptions.Select(redemption =>
                    new RecordSaleCreditNoteRedemptionIdempotencyPayload(
                        Normalize(redemption.Code),
                        redemption.Amount)).ToList()
                : [],
            command.CreditNoteCustomerMismatchConfirmed,
            command.Items.Select(item =>
            {
                var itemDiscount = item.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m);
                return new RecordSaleItemIdempotencyPayload(
                    (int)item.LineType,
                    item.ServiceId,
                    item.Barcode,
                    item.BatchNumber,
                    item.ItemName,
                    item.Quantity,
                    item.CostPrice,
                    item.SalesPrice,
                    item.Mrp,
                    item.TaxRatePercent,
                    item.IsPriceIncludingTax,
                    item.InventoryBatchId,
                    itemDiscount.Type,
                    itemDiscount.Value,
                    item.ClientLineKey,
                    Normalize(item.HsnCode));
            }).ToList());

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record RecordSaleIdempotencyPayload(
    Guid ShopId,
    Guid ActorUserId,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    PaymentMethod PaymentMethod,
    decimal PaidAmount,
    decimal DueAmount,
    InstantDiscountType SaleDiscountType,
    decimal SaleDiscountValue,
    decimal CreditNoteAppliedAmount,
    IReadOnlyList<RecordSaleCreditNoteRedemptionIdempotencyPayload> CreditNoteRedemptions,
    bool CreditNoteCustomerMismatchConfirmed,
    IReadOnlyList<RecordSaleItemIdempotencyPayload> Items);


internal sealed record RecordSaleItemIdempotencyPayload(
    int LineType,
    Guid? ServiceId,
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
    InstantDiscountType ItemDiscountType,
    decimal ItemDiscountValue,
    string? ClientLineKey,
    string? HsnCode);

internal sealed record RecordSaleCreditNoteRedemptionIdempotencyPayload(
    string? Code,
    decimal Amount);
