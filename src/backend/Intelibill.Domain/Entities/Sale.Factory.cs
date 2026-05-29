using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed partial class Sale
{
    public static ErrorOr<Sale> Record(
        Guid shopId,
        Guid actorUserId,
        string idempotencyKey,
        string requestHash,
        string invoiceNumber,
        IReadOnlyList<SaleLineInput> lines,
        Guid? customerId,
        string? customerName,
        string? customerPhone,
        PaymentMethod paymentMethod,
        decimal paidAmount,
        decimal dueAmount,
        DateTimeOffset soldAt,
        Guid? configuredSaleRuleId = null,
        DiscountRuleType? configuredSaleRuleType = null,
        decimal? configuredSaleRulePercentage = null,
        decimal? configuredSaleRuleThresholdAmount = null,
        InstantDiscountType saleDiscountOverrideType = InstantDiscountType.None,
        decimal saleDiscountOverrideValue = 0m,
        IReadOnlyList<string>? warnings = null)
    {
        if (lines is null || lines.Count == 0)
        {
            return Errors.Sale.ItemsRequired;
        }

        if (paidAmount < 0)
        {
            return Errors.Sale.PaidAmountInvalid;
        }

        if (dueAmount < 0)
        {
            return Errors.Sale.DueAmountInvalid;
        }

        if (paymentMethod == PaymentMethod.Credit && dueAmount <= 0)
        {
            return Errors.Sale.CreditRequiresDueAmount;
        }

        if (dueAmount > 0 && !customerId.HasValue && string.IsNullOrWhiteSpace(customerPhone))
        {
            return Errors.Sale.CustomerIdentityRequiredForDue;
        }

        var subtotalBeforeDiscount = 0m;
        var totalBeforeDiscount = 0m;
        var totalDiscountAmount = 0m;
        var totalAmount = 0m;
        var totalTaxAmount = 0m;
        var saleItems = new List<SaleItem>(lines.Count);

        foreach (var line in lines)
        {
            var saleItemOrError = CreateSaleItem(shopId, line);
            if (saleItemOrError.IsError)
            {
                return saleItemOrError.Errors;
            }

            var saleItem = saleItemOrError.Value;
            subtotalBeforeDiscount += saleItem.PreTaxAmountBeforeDiscount;
            totalBeforeDiscount += saleItem.TotalAmount + saleItem.ItemDiscountAmount + saleItem.SaleDiscountAmount;
            totalDiscountAmount += saleItem.ItemDiscountAmount + saleItem.SaleDiscountAmount;
            totalAmount += saleItem.TotalAmount;
            totalTaxAmount += saleItem.TaxAmount;
            saleItems.Add(saleItem);
        }

        if (decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero) !=
            decimal.Round(paidAmount + dueAmount, 2, MidpointRounding.AwayFromZero))
        {
            return Errors.Sale.PaidAndDueAmountMismatch;
        }

        return new Sale
        {
            ShopId = shopId,
            ActorUserId = actorUserId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Warnings = warnings?.ToArray() ?? [],
            InvoiceNumber = invoiceNumber,
            Source = SaleSource.Online,
            ClientSaleId = null,
            DeviceId = null,
            SyncedAt = null,
            CustomerId = customerId,
            CustomerName = NormalizeOptional(customerName),
            CustomerPhone = NormalizeOptional(customerPhone),
            PaymentMethod = paymentMethod,
            SoldAt = soldAt,
            PaidAmount = paidAmount,
            DueAmount = dueAmount,
            SubtotalBeforeDiscount = decimal.Round(subtotalBeforeDiscount, 2, MidpointRounding.AwayFromZero),
            TotalBeforeDiscount = decimal.Round(totalBeforeDiscount, 2, MidpointRounding.AwayFromZero),
            TotalDiscountAmount = decimal.Round(totalDiscountAmount, 2, MidpointRounding.AwayFromZero),
            TotalAmount = totalAmount,
            TotalTaxAmount = totalTaxAmount,
            ConfiguredSaleRuleId = configuredSaleRuleId,
            ConfiguredSaleRuleType = configuredSaleRuleType,
            ConfiguredSaleRulePercentage = configuredSaleRulePercentage,
            ConfiguredSaleRuleThresholdAmount = configuredSaleRuleThresholdAmount,
            SaleDiscountOverrideType = saleDiscountOverrideType,
            SaleDiscountOverrideValue = saleDiscountOverrideValue,
        }.WithItems(saleItems);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ErrorOr<SaleItem> CreateSaleItem(Guid shopId, SaleLineInput line)
    {
        if (line.LineType == SaleLineType.Goods)
        {
            if (!line.ItemId.HasValue || !line.InventoryBatchId.HasValue || line.ServiceId.HasValue)
                return Errors.Sale.InvalidLineReferences;

            return SaleItem.CreateGoods(
                shopId,
                line.ItemId.Value,
                line.InventoryBatchId.Value,
                line.LineName,
                line.LineCode,
                line.Quantity,
                line.CostPrice,
                line.SalesPrice,
                line.Mrp,
                line.TaxRatePercent,
                line.IsPriceIncludingTax,
                line.HasPriceMismatch,
                preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
                itemDiscountAmount: line.ItemDiscountAmount,
                saleDiscountAmount: line.SaleDiscountAmount,
                taxableAmount: line.TaxableAmount,
                taxAmount: line.TaxAmount,
                totalAmount: line.TotalAmount,
                configuredBatchRuleId: line.ConfiguredBatchRuleId,
                configuredBatchRulePercentage: line.ConfiguredBatchRulePercentage,
                itemDiscountOverrideType: line.ItemDiscountOverrideType,
                itemDiscountOverrideValue: line.ItemDiscountOverrideValue,
                hsnCode: line.HsnCode);
        }

        if (line.LineType == SaleLineType.Service)
        {
            if (line.ServiceId is null || line.ItemId.HasValue || line.InventoryBatchId.HasValue)
                return Errors.Sale.InvalidLineReferences;

            return SaleItem.CreateService(
                shopId,
                line.ServiceId.Value,
                line.LineName,
                line.LineCode,
                line.Quantity,
                line.CostPrice,
                line.SalesPrice,
                line.Mrp,
                line.TaxRatePercent,
                line.IsPriceIncludingTax,
                line.HasPriceMismatch,
                preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
                itemDiscountAmount: line.ItemDiscountAmount,
                saleDiscountAmount: line.SaleDiscountAmount,
                taxableAmount: line.TaxableAmount,
                taxAmount: line.TaxAmount,
                totalAmount: line.TotalAmount,
                itemDiscountOverrideType: line.ItemDiscountOverrideType,
                itemDiscountOverrideValue: line.ItemDiscountOverrideValue,
                hsnCode: line.HsnCode);
        }

        return Errors.Sale.InvalidLineReferences;
    }

}
