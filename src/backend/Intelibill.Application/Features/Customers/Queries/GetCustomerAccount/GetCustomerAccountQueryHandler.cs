using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Application.Features.Payments.Services;
using BusinessCalendar = Intelibill.Domain.Common.BusinessCalendar;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;

public sealed class GetCustomerAccountQueryHandler(
    IUserRepository userRepository,
    ICustomerRepository customerRepository,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IInvoiceBalanceReader invoiceBalanceReader)
{
    public async Task<ErrorOr<CustomerAccountDto>> HandleAsync(GetCustomerAccountQuery query, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Customer.UserIsNotOwnerOrManager;

        if (query.From is { } windowStart && query.To is { } windowEnd && windowStart > windowEnd)
            return Errors.Customer.StatementWindowInvalid;

        var customer = await customerRepository.GetByShopAndIdAsync(query.ActiveShopId, query.CustomerId, cancellationToken);
        if (customer is null)
            return Errors.Customer.CustomerNotFound;

        var sales = await saleRepository.GetByCustomerAsync(query.ActiveShopId, query.CustomerId, cancellationToken);
        var ledgerEntries = await customerLedgerEntryRepository.GetByCustomerAsync(query.ActiveShopId, query.CustomerId, cancellationToken);
        var outstandingDue = await customerLedgerEntryRepository.GetCustomerBalanceAsync(query.ActiveShopId, query.CustomerId, cancellationToken);
        var invoiceBalances = (await invoiceBalanceReader.GetForCustomerAsync(query.ActiveShopId, query.CustomerId, null, cancellationToken))
            .ToDictionary(b => b.SaleId);
        var receipts = await invoiceBalanceReader.GetReceiptsAsync(query.ActiveShopId, PaymentPartyType.Customer, query.CustomerId, cancellationToken);
        var receiptByLedgerEntry = receipts.ToDictionary(r => r.LedgerEntryId, r => r.ReceiptId);

        // Oldest first, deterministically: entries on one date in the order they were written,
        // and the id settles any remaining tie.
        var ledgerChronological = ledgerEntries
            .OrderBy(e => e.EntryDate)
            .ThenBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

        // The opening balance is every signed entry before the window, never a displayed row's
        // after-entry balance.
        var openingBalance = query.From is { } from
            ? ledgerChronological.Where(e => e.EntryDate < from).Sum(e => e.EntryType.ToBalanceDelta(e.Amount))
            : 0m;
        var windowEntries = ledgerChronological
            .Where(e => (query.From is null || e.EntryDate >= query.From) && (query.To is null || e.EntryDate <= query.To))
            .ToList();

        var runningBalance = openingBalance;
        var ledgerDtos = new List<CustomerLedgerEntryDto>(windowEntries.Count);
        foreach (var entry in windowEntries)
        {
            runningBalance += entry.EntryType.ToBalanceDelta(entry.Amount);
            ledgerDtos.Add(new CustomerLedgerEntryDto(
                entry.Id,
                entry.SaleId,
                entry.EntryType,
                entry.Amount,
                entry.EntryDate,
                entry.Notes,
                runningBalance,
                receiptByLedgerEntry.TryGetValue(entry.Id, out var receiptId) ? receiptId : null));
        }

        var closingBalance = runningBalance;

        // Newest first for display; the reverse of the chronological order, so ties stay stable.
        var orderedLedgerDtos = Enumerable.Reverse(ledgerDtos).ToList();

        var paymentHistory = orderedLedgerDtos
            .Where(e => e.EntryType == CustomerLedgerEntryType.PaymentReceived)
            .ToList();

        var asOf = query.AsOf ?? BusinessCalendar.DateOf(DateTimeOffset.UtcNow);
        var saleDtos = sales.Select(s =>
        {
            var balance = invoiceBalances.GetValueOrDefault(s.Id);
            var currentOutstanding = balance?.OutstandingAmount ?? 0m;
            return new CustomerAccountSaleDto(
                s.Id,
                s.InvoiceNumber,
                s.PaymentMethod,
                s.SoldAt,
                s.PaidAmount,
                s.DueAmount,
                s.Items.Sum(i => i.TotalAmount),
                balance?.AllocatedPaymentAmount ?? 0m,
                balance?.ReturnCreditAmount ?? 0m,
                currentOutstanding)
            {
                DueDate = s.DueDate,
                IsOverdue = IsOverdue(currentOutstanding, s.DueDate, asOf),
            };
        }).ToList();

        var creditState = CustomerCreditState.Of(customer);
        return new CustomerAccountDto(
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            outstandingDue,
            saleDtos,
            orderedLedgerDtos,
            paymentHistory,
            receipts.Sum(r => r.UnallocatedAmount),
            receipts)
        {
            CreditLimit = customer.CreditLimit,
            IsActive = customer.IsActive,
            CreditPolicyMode = customer.CreditPolicyMode,
            CreditTermsDays = customer.CreditTermsDays,
            AvailableCredit = CustomerCreditPolicy.AvailableCredit(creditState, outstandingDue),
            OpeningBalance = openingBalance,
            ClosingBalance = closingBalance,
            FromDate = query.From,
            ToDate = query.To,
            AsOfBusinessDate = asOf,
            // Unallocated payments still pay down the account: overdue never exceeds what is owed.
            OverdueAmount = Math.Min(saleDtos.Where(s => s.IsOverdue).Sum(s => s.CurrentOutstandingAmount), Math.Max(0m, outstandingDue)),
        };
    }

    /// <summary>Overdue is a due-date fact: something is still owed and its due date has passed.</summary>
    internal static bool IsOverdue(decimal currentOutstanding, DateOnly? dueDate, DateOnly asOf) =>
        currentOutstanding > 0m && dueDate is { } due && due < asOf;
}
