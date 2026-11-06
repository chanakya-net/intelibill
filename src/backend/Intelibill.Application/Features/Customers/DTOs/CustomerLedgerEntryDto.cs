using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

/// <param name="RunningBalance">The account balance after this entry, from the window's opening balance.</param>
public sealed record CustomerLedgerEntryDto(
    Guid EntryId,
    Guid? SaleId,
    CustomerLedgerEntryType EntryType,
    decimal Amount,
    DateOnly EntryDate,
    string? Notes,
    decimal RunningBalance,
    Guid? ReceiptId = null)
{
    /// <summary>The entry's effect on what the customer owes: positive = debit, negative = credit.</summary>
    public decimal SignedAmount => EntryType.ToBalanceDelta(Amount);
}
