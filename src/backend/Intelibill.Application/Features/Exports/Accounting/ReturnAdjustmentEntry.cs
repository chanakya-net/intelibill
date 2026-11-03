using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Exports.Accounting;

public enum ReturnAdjustmentEntryKind
{
    /// <summary>The return as recorded, posted on the day it was processed.</summary>
    Return = 1,

    /// <summary>The return's void, posted on the day it was voided as the exact opposite of the return.</summary>
    VoidReversal = 2,
}

/// <summary>
/// One dated accounting posting of a sale return, read from its posted snapshot. Every export format
/// and the P&amp;L read these and nothing else for return figures, so they cannot disagree.
/// Amounts are signed: a void reversal carries the negatives of its return.
/// </summary>
public sealed record ReturnAdjustmentEntry(
    ReturnAdjustmentEntryKind Kind,
    DateTimeOffset PostedAt,
    Guid SaleReturnId,
    string ReturnNumber,
    DateTimeOffset ReturnProcessedAt,
    Guid SaleId,
    string InvoiceNumber,
    string? CustomerName,
    ReturnAccountingStatus Status,
    string? PolicyVersion,
    string? ReviewReason,
    decimal OriginalReturnedGross,
    decimal FinancialAdjustment,
    decimal? TaxableAdjustment,
    decimal? TaxAdjustment,
    decimal? NonTaxAdjustment,
    IReadOnlyList<ReturnAdjustmentTaxComponent> TaxComponents,
    IReadOnlyList<ReturnAdjustmentSettlement> Settlements,
    IReadOnlyList<ReturnAdjustmentLine> Lines,
    string? VoidReason)
{
    public bool IsReversal => Kind == ReturnAdjustmentEntryKind.VoidReversal;

    /// <summary>The return number, marked when this is its void's reversal.</summary>
    public string Label => IsReversal ? $"{ReturnNumber} (void)" : ReturnNumber;

    /// <summary>Balanced and moving money: only these become vouchers. A zero refund keeps its stock fact only.</summary>
    public bool HasVoucher => Status == ReturnAccountingStatus.Balanced && FinancialAdjustment != 0m;

    public decimal SettledBy(ReturnSettlementKind kind) =>
        Settlements.Where(s => s.Kind == kind).Sum(s => s.Amount);
}

/// <summary>Taxable value and output tax adjusted at one rate.</summary>
public sealed record ReturnAdjustmentTaxComponent(
    decimal TaxRatePercent,
    decimal TaxableAmount,
    decimal TaxAmount);

public sealed record ReturnAdjustmentSettlement(
    ReturnSettlementKind Kind,
    decimal Amount,
    Guid? CustomerId,
    Guid? CreditNoteId,
    string? CreditNoteCode);

/// <summary>The physical side of one returned line (quantity, condition, original cost and value) with its adjustment.</summary>
public sealed record ReturnAdjustmentLine(
    Guid SaleItemId,
    SaleLineType? LineType,
    SaleReturnCondition? Condition,
    decimal Quantity,
    decimal OriginalCostPrice,
    decimal OriginalReturnedGross,
    decimal TaxRatePercent,
    decimal? TaxableAdjustment,
    decimal? TaxAdjustment,
    decimal? NonTaxAdjustment);
