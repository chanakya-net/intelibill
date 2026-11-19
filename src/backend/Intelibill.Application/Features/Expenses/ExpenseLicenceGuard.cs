using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Expenses;

/// <summary>
/// Licence rules for expense writes. Both entry points take the licence fence first, inside the
/// source transaction, before the posting-period and till locks.
/// </summary>
internal static class ExpenseLicenceGuard
{
    /// <summary>A new independent expense needs operating access.</summary>
    public static Task<Error?> DenyNewExpenseAsync(IShopLicenceFence fence, Guid shopId, CancellationToken ct) =>
        fence.ReadTradingDenialAsync(shopId, ct);

    /// <summary>
    /// Enters the fence for a correction. Returns a refusal, or whether the shop is restricted so the
    /// caller must prove the source once it holds the expense row lock.
    /// </summary>
    public static async Task<ErrorOr<bool>> EnterCorrectionAsync(IShopLicenceFence fence, Guid shopId, CancellationToken ct)
    {
        var lease = await fence.EnterAsync(shopId, LicenceAction.SourceCorrection, ct);
        if (lease.IsError)
            return lease.Errors;
        if (lease.Value.SuspensionDenial() is { } suspended)
            return suspended;

        var decision = lease.Value.CurrentDecision();
        if (decision is { Enforced: true, Outcome: LicenceOutcome.Blocked })
            return decision.Denial ?? Errors.Licence.VerificationUnavailable;
        return decision is { Enforced: true, Outcome: LicenceOutcome.RequiresProvenSource };
    }

    /// <summary>
    /// Restricted shops may restate an eligible source only: same date, no higher amount, so a
    /// correction (or a chain of them) can never carry a new or larger expense.
    /// </summary>
    public static Error? ProveSource(Expense original, decimal amount, DateOnly expenseDate)
    {
        if (original.Treatment == ExpenseTreatment.SupplierSettlement || original.SupplierLedgerEntryId.HasValue)
            return Errors.Expense.CorrectionSourceIneligible("supplier settlements are corrected through their payment receipt.");
        if (expenseDate != original.ExpenseDate)
            return Errors.Expense.CorrectionSourceIneligible("the expense date must stay the original date.");
        if (amount > original.Amount)
            return Errors.Expense.CorrectionSourceIneligible("the amount cannot exceed the original amount.");
        return null;
    }
}
