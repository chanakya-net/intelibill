using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

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
    public decimal TotalAmount { get; private set; }
    public decimal TotalTaxAmount { get; private set; }

    public IReadOnlyList<SaleItem> Items => _items.AsReadOnly();

    private Sale() { }

    public static Sale Create(
        Guid shopId,
        string invoiceNumber,
        Guid? customerId,
        string? customerName,
        string? customerPhone,
        PaymentMethod paymentMethod,
        DateTimeOffset soldAt,
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
            TotalAmount = totalAmount,
            TotalTaxAmount = totalTaxAmount,
        };
        sale._items.AddRange(items);
        return sale;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
