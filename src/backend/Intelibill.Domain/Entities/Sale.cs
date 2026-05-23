using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed partial class Sale : BaseEntity
{
    private readonly List<SaleItem> _items = [];

    public Guid ShopId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string[] Warnings { get; private set; } = [];
    public string InvoiceNumber { get; private set; } = string.Empty;
    public SaleSource Source { get; private set; }
    public string? ClientSaleId { get; private set; }
    public string? DeviceId { get; private set; }
    public DateTimeOffset? SyncedAt { get; private set; }
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

    private Sale WithItems(IReadOnlyCollection<SaleItem> items)
    {
        _items.AddRange(items);
        return this;
    }
}
