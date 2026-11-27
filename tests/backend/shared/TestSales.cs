using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Tests.Fixtures;

/// <summary>
/// Sales for fixtures that need no actor or request identity: a fresh idempotency key and request
/// hash each, and the business day of the instant in the default zone.
/// </summary>
internal static class TestSales
{
    public static Sale Create(Guid shopId, string invoiceNumber, Guid? customerId, string? customerName, string? customerPhone, PaymentMethod paymentMethod, DateTimeOffset soldAt, decimal paidAmount, decimal dueAmount, decimal totalAmount, decimal totalTaxAmount, IReadOnlyList<SaleItem> items, decimal? subtotalBeforeDiscount = null, decimal? totalBeforeDiscount = null, decimal totalDiscountAmount = 0m, Guid? configuredSaleRuleId = null, DiscountRuleType? configuredSaleRuleType = null, decimal? configuredSaleRulePercentage = null, decimal? configuredSaleRuleThresholdAmount = null, InstantDiscountType saleDiscountOverrideType = InstantDiscountType.None, decimal saleDiscountOverrideValue = 0m, SaleSource source = SaleSource.Online, string? clientSaleId = null, string? deviceId = null, DateTimeOffset? syncedAt = null) =>
        Sale.Create(shopId, Guid.Empty, $"fixture-{Guid.NewGuid():N}", $"fixture-{Guid.NewGuid():N}", invoiceNumber, customerId, customerName, customerPhone, paymentMethod, soldAt, BusinessDay.InDefaultZone(soldAt), paidAmount, dueAmount, totalAmount, totalTaxAmount, items, subtotalBeforeDiscount, totalBeforeDiscount, totalDiscountAmount, configuredSaleRuleId, configuredSaleRuleType, configuredSaleRulePercentage, configuredSaleRuleThresholdAmount, saleDiscountOverrideType, saleDiscountOverrideValue, source, clientSaleId, deviceId, syncedAt);
}
