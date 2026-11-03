using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

/// <summary>How one return line's approved refund divides into taxable value, output tax and a commercial-only remainder.</summary>
public sealed record ReturnLineAccounting(
    Guid SaleItemId,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal NonTaxAmount)
{
    public decimal Total => TaxableAmount + TaxAmount + NonTaxAmount;
}

/// <summary>One leg of how the approved refund reached the customer.</summary>
public sealed record ReturnSettlementInput(
    ReturnSettlementKind Kind,
    decimal Amount,
    Guid? CustomerId = null,
    Guid? CreditNoteId = null,
    Guid? CustomerLedgerEntryId = null);

/// <summary>
/// The financial side of a sale return as posted: which policy classified it, the per-line split and
/// the settlement legs. Kept apart from the physical return (quantity, condition, original value),
/// which stock and cost of goods read instead. Internally consistent by construction; whether it
/// matches its return is checked when the return posts it.
/// </summary>
public sealed class ReturnAccountingSnapshot
{
    public const int MaxPolicyVersionLength = 40;

    private ReturnAccountingSnapshot(
        string policyVersion,
        IReadOnlyList<ReturnLineAccounting> lines,
        IReadOnlyList<ReturnSettlementInput> settlements)
    {
        PolicyVersion = policyVersion;
        Lines = lines;
        Settlements = settlements;
    }

    public string PolicyVersion { get; }
    public IReadOnlyList<ReturnLineAccounting> Lines { get; }
    public IReadOnlyList<ReturnSettlementInput> Settlements { get; }

    public decimal TaxableAmount => Lines.Sum(l => l.TaxableAmount);
    public decimal TaxAmount => Lines.Sum(l => l.TaxAmount);
    public decimal NonTaxAmount => Lines.Sum(l => l.NonTaxAmount);
    public decimal FinancialAdjustment => Lines.Sum(l => l.Total);
    public decimal SettledAmount => Settlements.Sum(s => s.Amount);

    public static ErrorOr<ReturnAccountingSnapshot> Create(
        string policyVersion,
        IReadOnlyList<ReturnLineAccounting> lines,
        IReadOnlyList<ReturnSettlementInput> settlements)
    {
        if (string.IsNullOrWhiteSpace(policyVersion) || policyVersion.Trim().Length > MaxPolicyVersionLength)
            return Errors.Sale.ReturnAccountingPolicyRequired;

        if (lines.Count == 0 || lines.Select(l => l.SaleItemId).Distinct().Count() != lines.Count)
            return Errors.Sale.ReturnAccountingLineMismatch;

        if (lines.Any(l => !IsMoney(l.TaxableAmount) || !IsMoney(l.TaxAmount) || !IsMoney(l.NonTaxAmount)))
            return Errors.Sale.ReturnAccountingAmountInvalid;

        foreach (var settlement in settlements)
        {
            if (!IsMoney(settlement.Amount) || settlement.Amount == 0m || !Enum.IsDefined(settlement.Kind))
                return Errors.Sale.ReturnAccountingSettlementInvalid;

            var referencesCreditNote = settlement.CreditNoteId.HasValue;
            if (referencesCreditNote != (settlement.Kind == ReturnSettlementKind.StoreCreditIssued))
                return Errors.Sale.ReturnAccountingSettlementInvalid;

            if (settlement.CustomerLedgerEntryId.HasValue && settlement.Kind != ReturnSettlementKind.ReceivableReduction)
                return Errors.Sale.ReturnAccountingSettlementInvalid;
        }

        var snapshot = new ReturnAccountingSnapshot(policyVersion.Trim(), lines.ToList(), settlements.ToList());
        if (snapshot.SettledAmount != snapshot.FinancialAdjustment)
            return Errors.Sale.ReturnAccountingSettlementMismatch;

        return snapshot;
    }

    private static bool IsMoney(decimal value) =>
        value >= 0m && decimal.Round(value, 2) == value;
}
