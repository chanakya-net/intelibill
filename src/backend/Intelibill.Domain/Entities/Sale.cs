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
    public decimal TotalAmount { get; private set; }
    public decimal TotalTaxAmount { get; private set; }

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
        DateTimeOffset soldAt)
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

        var totalAmount = 0m;
        var totalTaxAmount = 0m;
        var saleItems = new List<SaleItem>(lines.Count);

        foreach (var line in lines)
        {
            var taxAmount = CalculateTaxAmount(line);
            var lineTotal = line.SalesPrice * line.Quantity;
            if (!line.IsPriceIncludingTax)
            {
                lineTotal += taxAmount;
            }

            totalAmount += lineTotal;
            totalTaxAmount += taxAmount;
            saleItems.Add(CreateSaleItem(shopId, line));
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
            TotalAmount = totalAmount,
            TotalTaxAmount = totalTaxAmount,
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
        IReadOnlyList<SaleItem> items)
    {
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
            TotalAmount = totalAmount,
            TotalTaxAmount = totalTaxAmount,
        };
        sale._items.AddRange(items);
        return sale;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal CalculateTaxAmount(SaleLineInput line)
    {
        if (line.IsPriceIncludingTax && line.TaxRatePercent > 0)
        {
            return line.Quantity * line.SalesPrice * line.TaxRatePercent / (100m + line.TaxRatePercent);
        }

        return line.Quantity * line.SalesPrice * line.TaxRatePercent / 100m;
    }

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
            line.HasPriceMismatch);

    private Sale WithItems(IReadOnlyCollection<SaleItem> items)
    {
        _items.AddRange(items);
        return this;
    }
}
