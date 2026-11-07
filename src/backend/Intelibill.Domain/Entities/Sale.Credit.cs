using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed partial class Sale
{
    /// <summary>
    /// The business date the sale's due falls due, fixed at issue from the customer's agreed terms.
    /// Null when the sale has no due, no terms were agreed, or it was issued before terms existed:
    /// such due is outstanding, never overdue.
    /// </summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>Why an owner or manager let this sale exceed the customer's enforced limit.</summary>
    public string? CreditOverrideReason { get; private set; }

    public Guid? CreditOverrideByUserId { get; private set; }

    /// <summary>The account exposure the approver saw when they approved the override.</summary>
    public decimal? CreditOverrideObservedExposure { get; private set; }

    /// <summary>The enforced limit in force when the override was approved.</summary>
    public decimal? CreditOverrideLimit { get; private set; }

    /// <summary>Fixes the due date from agreed terms. Set once, at issue.</summary>
    public void ApplyCreditTerms(int? creditTermsDays)
    {
        if (DueAmount <= 0m || creditTermsDays is null or < 0)
            return;

        DueDate = BusinessDate.AddDays(creditTermsDays.Value);
    }

    /// <summary>Records who approved going over the limit, why, and what they saw.</summary>
    public void RecordCreditOverride(string reason, Guid approvedByUserId, decimal observedExposure, decimal limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        CreditOverrideReason = reason.Trim();
        CreditOverrideByUserId = approvedByUserId;
        CreditOverrideObservedExposure = observedExposure;
        CreditOverrideLimit = limit;
    }
}
