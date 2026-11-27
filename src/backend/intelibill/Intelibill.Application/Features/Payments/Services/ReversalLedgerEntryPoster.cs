using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>Adds the reversing ledger entry for a reversed payment's party, voiding a supplier payment's expense.</summary>
public sealed class ReversalLedgerEntryPoster(
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IExpenseRepository expenseRepository)
{
    /// <summary>Adds the reversing ledger entry for the party (voiding a supplier payment's expense) and returns its id.</summary>
    internal async Task<ErrorOr<Guid>> PostReversalLedgerEntryAsync(
        PaymentReceipt receipt,
        string reason,
        DateOnly businessDate,
        Guid actorUserId,
        List<Expense> expensesToVoid,
        CancellationToken cancellationToken)
    {
        var notes = $"Reversal of payment {receipt.Id:N}: {reason}";
        if (notes.Length > 255)
            notes = notes[..255];

        if (receipt.PartyType == PaymentPartyType.Customer)
        {
            var entry = CustomerLedgerEntry.Create(
                receipt.ShopId,
                receipt.PartyId,
                saleId: null,
                CustomerLedgerEntryType.PaymentReversal,
                receipt.Amount,
                businessDate,
                notes,
                actorUserId);
            if (entry.IsError)
                return entry.Errors;

            await customerLedgerEntryRepository.AddAsync(entry.Value, cancellationToken);
            return entry.Value.Id;
        }
        else
        {
            var entry = SupplierLedgerEntry.Create(
                receipt.ShopId,
                receipt.PartyId,
                batchId: null,
                SupplierLedgerEntryType.Reversal,
                receipt.Amount,
                businessDate,
                notes,
                actorUserId);
            if (entry.IsError)
                return entry.Errors;

            await supplierLedgerEntryRepository.AddAsync(entry.Value, cancellationToken);
            var ledgerEntryId = entry.Value.Id;

            foreach (var expense in expensesToVoid)
            {
                expense.MarkVoided();
                expenseRepository.Update(expense);
            }

            return ledgerEntryId;
        }
    }
}
