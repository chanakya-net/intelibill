namespace Intelibill.Domain.Enums;

public enum ReconciliationIssueType
{
    StockVariance = 1,
    InvoiceConflict = 2,
    ValidationConflict = 3,
    PricingVariance = 4,
    DiscountVariance = 5,
    CustomerVariance = 6,
}
