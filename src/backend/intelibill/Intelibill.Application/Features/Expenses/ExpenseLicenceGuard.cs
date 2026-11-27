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
    public static Error? TradingDenial(ShopLicenceLease lease) => lease.CurrentDecision().EnforcedDenial();

    /// <summary>
    /// Enters the fence for a correction. The caller reevaluates the returned lease after business locks.
    /// </summary>
    public static async Task<ErrorOr<ShopLicenceLease>> EnterCorrectionAsync(IShopLicenceFence fence, Guid shopId, CancellationToken ct)
    {
        var lease = await fence.EnterAsync(shopId, LicenceAction.SourceCorrection, ct);
        if (lease.IsError)
            return lease.Errors;
        if (lease.Value.SuspensionDenial() is { } suspended)
            return suspended;

        var decision = lease.Value.CurrentDecision();
        if (decision is { Enforced: true, Outcome: LicenceOutcome.Blocked })
            return decision.Denial ?? Errors.Licence.VerificationUnavailable;
        return lease.Value;
    }

    /// <summary>
    /// Restricted corrections retain the source identity and date and cannot increase its amount.
    /// </summary>
    public static Error? ProveSource(Expense original, decimal amount, DateOnly expenseDate, string paidTo, string? description)
    {
        if (original.Treatment == ExpenseTreatment.SupplierSettlement || original.SupplierLedgerEntryId.HasValue)
            return Errors.Expense.CorrectionSourceIneligible("supplier settlements are corrected through their payment receipt.");
        if (expenseDate != original.ExpenseDate)
            return Errors.Expense.CorrectionSourceIneligible("the expense date must stay the original date.");
        if (amount > original.Amount)
            return Errors.Expense.CorrectionSourceIneligible("the amount cannot exceed the original amount.");
        if (!SameText(original.PaidTo, paidTo) || !SameText(original.Description, description))
            return Errors.Expense.CorrectionSourceIneligible("the payee and obligation must match the original expense.");
        return null;
    }

    private static bool SameText(string? left, string? right) =>
        string.Equals(left?.Trim() ?? "", right?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
}
