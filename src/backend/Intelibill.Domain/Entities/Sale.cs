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
    public decimal CreditNoteAppliedAmount { get; private set; }

    public IReadOnlyList<SaleItem> Items => _items.AsReadOnly();

    private Sale() { }

    internal static Sale Create(Guid shopId, Guid actorUserId, string idempotencyKey, string requestHash, string invoiceNumber, Guid? customerId, string? customerName, string? customerPhone, PaymentMethod paymentMethod, DateTimeOffset soldAt, decimal paidAmount, decimal dueAmount, decimal totalAmount, decimal totalTaxAmount, IReadOnlyList<SaleItem> items, decimal? subtotalBeforeDiscount = null, decimal? totalBeforeDiscount = null, decimal totalDiscountAmount = 0m, Guid? configuredSaleRuleId = null, DiscountRuleType? configuredSaleRuleType = null, decimal? configuredSaleRulePercentage = null, decimal? configuredSaleRuleThresholdAmount = null, InstantDiscountType saleDiscountOverrideType = InstantDiscountType.None, decimal saleDiscountOverrideValue = 0m, SaleSource source = SaleSource.Online, string? clientSaleId = null, string? deviceId = null, DateTimeOffset? syncedAt = null, IReadOnlyList<string>? warnings = null, decimal creditNoteAppliedAmount = 0m)
    {
        var effectiveSubtotalBeforeDiscount = subtotalBeforeDiscount ?? decimal.Round(items.Sum(i => i.PreTaxAmountBeforeDiscount), 2, MidpointRounding.AwayFromZero);
        var effectiveTotalBeforeDiscount = totalBeforeDiscount ?? decimal.Round(items.Sum(i => i.TotalAmount + i.ItemDiscountAmount + i.SaleDiscountAmount), 2, MidpointRounding.AwayFromZero);

        var sale = new Sale
        {
            ShopId = shopId,
            ActorUserId = actorUserId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Warnings = warnings?.ToArray() ?? [],
            InvoiceNumber = invoiceNumber,
            Source = source,
            ClientSaleId = NormalizeOptional(clientSaleId),
            DeviceId = NormalizeOptional(deviceId),
            SyncedAt = syncedAt,
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
            CreditNoteAppliedAmount = creditNoteAppliedAmount,
        };
        sale._items.AddRange(items);
        return sale;
    }

    internal static Sale Create(Guid shopId, string invoiceNumber, Guid? customerId, string? customerName, string? customerPhone, PaymentMethod paymentMethod, DateTimeOffset soldAt, decimal paidAmount, decimal dueAmount, decimal totalAmount, decimal totalTaxAmount, IReadOnlyList<SaleItem> items, decimal? subtotalBeforeDiscount = null, decimal? totalBeforeDiscount = null, decimal totalDiscountAmount = 0m, Guid? configuredSaleRuleId = null, DiscountRuleType? configuredSaleRuleType = null, decimal? configuredSaleRulePercentage = null, decimal? configuredSaleRuleThresholdAmount = null, InstantDiscountType saleDiscountOverrideType = InstantDiscountType.None, decimal saleDiscountOverrideValue = 0m, SaleSource source = SaleSource.Online, string? clientSaleId = null, string? deviceId = null, DateTimeOffset? syncedAt = null)
    {
        var idempotencyKey = $"legacy-{Guid.NewGuid():N}";
        var requestHash = $"legacy-{Guid.NewGuid():N}";
        return Create(shopId, Guid.Empty, idempotencyKey, requestHash, invoiceNumber, customerId, customerName, customerPhone, paymentMethod, soldAt, paidAmount, dueAmount, totalAmount, totalTaxAmount, items, subtotalBeforeDiscount, totalBeforeDiscount, totalDiscountAmount, configuredSaleRuleId, configuredSaleRuleType, configuredSaleRulePercentage, configuredSaleRuleThresholdAmount, saleDiscountOverrideType, saleDiscountOverrideValue, source, clientSaleId, deviceId, syncedAt);
    }

    private Sale WithItems(IReadOnlyCollection<SaleItem> items)
    {
        _items.AddRange(items);
        return this;
    }
}
