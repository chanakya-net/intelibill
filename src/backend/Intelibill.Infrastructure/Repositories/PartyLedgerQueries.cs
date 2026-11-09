using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Repositories;

// Keep these expressions translatable: only one aggregate row per party leaves PostgreSQL.
internal static class PartyLedgerQueries
{
    internal sealed class Balance
    {
        public Guid PartyId { get; init; }
        public decimal Amount { get; init; }
        public int UnknownTypes { get; init; }
    }

    internal static IQueryable<Balance> Customers(IQueryable<CustomerLedgerEntry> entries) => entries
        .GroupBy(e => e.CustomerId)
        .Select(g => new Balance { PartyId = g.Key,
            Amount = g.Sum(e => e.EntryType == CustomerLedgerEntryType.SaleDue
                || e.EntryType == CustomerLedgerEntryType.ReturnCreditReversal
                || e.EntryType == CustomerLedgerEntryType.PaymentReversal ? e.Amount
                : e.EntryType == CustomerLedgerEntryType.PaymentReceived
                    || e.EntryType == CustomerLedgerEntryType.ReturnCredit
                    || e.EntryType == CustomerLedgerEntryType.SaleCancellationCredit ? -e.Amount : 0m),
            UnknownTypes = g.Sum(e => e.EntryType == CustomerLedgerEntryType.SaleDue
                || e.EntryType == CustomerLedgerEntryType.ReturnCreditReversal
                || e.EntryType == CustomerLedgerEntryType.PaymentReversal
                || e.EntryType == CustomerLedgerEntryType.PaymentReceived
                || e.EntryType == CustomerLedgerEntryType.ReturnCredit
                || e.EntryType == CustomerLedgerEntryType.SaleCancellationCredit ? 0 : 1) });

    internal static IQueryable<Balance> Suppliers(IQueryable<SupplierLedgerEntry> entries) => entries
        .GroupBy(e => e.SupplierId)
        .Select(g => new Balance { PartyId = g.Key, Amount = g.Sum(e => e.EntryType == SupplierLedgerEntryType.PaymentMade ? -e.Amount : e.Amount), UnknownTypes = 0 });

    internal static void RejectUnknown(int count)
    {
        if (count != 0) throw new ArgumentOutOfRangeException(nameof(count), "Unknown customer ledger entry type.");
    }
}
