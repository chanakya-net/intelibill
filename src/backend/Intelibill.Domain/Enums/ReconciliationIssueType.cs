namespace Intelibill.Domain.Enums;

public enum ReconciliationIssueType
{
    StockVariance = 1,
    InvoiceConflict = 2,
    ValidationConflict = 3,
    PricingVariance = 4,
    DiscountVariance = 5,
    CustomerVariance = 6,
    ArithmeticConflict = 7,
    IssueTimingReview = 8,
    ProvenanceReview = 9,
    RecoveryReview = 10,

    /// <summary>An offline sale posted without a GST document: the profile it was issued under could not issue one.</summary>
    DocumentReview = 11,

    /// <summary>An offline invoice issued in a month that is now closed: it posts only after an owner reopens the month.</summary>
    ClosedPeriodReview = 12,

    TillReview = 13,
}
