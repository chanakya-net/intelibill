namespace Intelibill.Application.Features.Exports.Accounting;

/// <summary>
/// A double-entry voucher, format-neutral, as it must balance before any renderer writes it.
/// <see cref="Reference"/> names the document it adjusts or reverses, dated <see cref="ReferenceDate"/>.
/// </summary>
public sealed record AccountingVoucher(
    string Number,
    string Type,
    DateOnly Date,
    string Reference,
    DateOnly ReferenceDate,
    string? Narration,
    IReadOnlyList<AccountingVoucherLine> Lines)
{
    public decimal Debits => Lines.Where(l => l.IsDebit).Sum(l => l.Amount);
    public decimal Credits => Lines.Where(l => !l.IsDebit).Sum(l => l.Amount);
}

public sealed record AccountingVoucherLine(string Ledger, decimal Amount, bool IsDebit);
