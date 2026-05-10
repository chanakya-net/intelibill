using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class Sale : BaseEntity
{
    private readonly List<SaleItem> _items = [];

    public Guid ShopId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public DateTimeOffset SoldAt { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal DueAmount { get; private set; }
    public decimal SubtotalBeforeDiscount { get; private set; }
    public decimal TotalBeforeDiscount { get; private set; }
    public decimal TotalDiscountAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal TotalTaxAmount { get; private set; }
    public Guid? ConfiguredSaleRuleId { get; private set; }
    public DiscountRuleType? ConfiguredSaleRuleType { get; private set; }
    public decimal? ConfiguredSaleRulePercentage { get; private set; }
    public decimal? ConfiguredSaleRuleThresholdAmount { get; private set; }
    public InstantDiscountType SaleDiscountOverrideType { get; private set; }
    public decimal SaleDiscountOverrideValue { get; private set; }

    public IReadOnlyList<SaleItem> Items => _items.AsReadOnly();

    private Sale() { }

    public static ErrorOr<Sale> Record(
        Guid shopId,
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
        decimal saleDiscountOverrideValue = 0m)
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
            var saleItem = CreateSaleItem(shopId, line);
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
            InvoiceNumber = invoiceNumber,
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

    internal static Sale Create(
        Guid shopId,
        string invoiceNumber,
        Guid? customerId,
        string? customerName,
        string? customerPhone,
        PaymentMethod paymentMethod,
        DateTimeOffset soldAt,
        decimal paidAmount,
        decimal dueAmount,
        decimal totalAmount,
        decimal totalTaxAmount,
        IReadOnlyList<SaleItem> items,
        decimal? subtotalBeforeDiscount = null,
        decimal? totalBeforeDiscount = null,
        decimal totalDiscountAmount = 0m,
        Guid? configuredSaleRuleId = null,
        DiscountRuleType? configuredSaleRuleType = null,
        decimal? configuredSaleRulePercentage = null,
        decimal? configuredSaleRuleThresholdAmount = null,
        InstantDiscountType saleDiscountOverrideType = InstantDiscountType.None,
        decimal saleDiscountOverrideValue = 0m)
    {
        var effectiveSubtotalBeforeDiscount = subtotalBeforeDiscount
            ?? decimal.Round(items.Sum(i => i.PreTaxAmountBeforeDiscount), 2, MidpointRounding.AwayFromZero);
        var effectiveTotalBeforeDiscount = totalBeforeDiscount
            ?? decimal.Round(items.Sum(i => i.TotalAmount + i.ItemDiscountAmount + i.SaleDiscountAmount), 2, MidpointRounding.AwayFromZero);

        var sale = new Sale
        {
            ShopId = shopId,
            InvoiceNumber = invoiceNumber,
            CustomerId = customerId,
            CustomerName = NormalizeOptional(customerName),
            CustomerPhone = NormalizeOptional(customerPhone),
            PaymentMethod = paymentMethod,
            SoldAt = soldAt,
            PaidAmount = paidAmount,
            DueAmount = dueAmount,
            SubtotalBeforeDiscount = effectiveSubtotalBeforeDiscount,
            TotalBeforeDiscount = effectiveTotalBeforeDiscount,
            TotalDiscountAmount = totalDiscountAmount,
            TotalAmount = totalAmount,
            TotalTaxAmount = totalTaxAmount,
            ConfiguredSaleRuleId = configuredSaleRuleId,
            ConfiguredSaleRuleType = configuredSaleRuleType,
            ConfiguredSaleRulePercentage = configuredSaleRulePercentage,
            ConfiguredSaleRuleThresholdAmount = configuredSaleRuleThresholdAmount,
            SaleDiscountOverrideType = saleDiscountOverrideType,
            SaleDiscountOverrideValue = saleDiscountOverrideValue,
        };
        sale._items.AddRange(items);
        return sale;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SaleItem CreateSaleItem(Guid shopId, SaleLineInput line) =>
        SaleItem.Create(
            shopId,
            line.ItemId,
            line.InventoryBatchId,
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
            itemDiscountOverrideValue: line.ItemDiscountOverrideValue);

    private Sale WithItems(IReadOnlyCollection<SaleItem> items)
    {
        _items.AddRange(items);
        return this;
    }
}
