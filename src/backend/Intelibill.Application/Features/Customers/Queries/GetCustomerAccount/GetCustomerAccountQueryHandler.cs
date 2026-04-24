using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;

public sealed class GetCustomerAccountQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ICustomerRepository customerRepository,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
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

        var shop = await shopRepository.GetByIdWithMembersAsync(query.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var ownerMembership = shop.Memberships.FirstOrDefault(sm => sm.Role == ShopRole.Owner);
        if (ownerMembership is null)
            return Errors.Customer.ShopOwnerNotFound;

        var customer = await customerRepository.GetByOwnerAndIdAsync(ownerMembership.UserId, query.CustomerId, cancellationToken);
        if (customer is null)
            return Errors.Customer.CustomerNotFound;

        var sales = await saleRepository.GetByCustomerAsync(query.ActiveShopId, query.CustomerId, cancellationToken);
        var ledgerEntries = await customerLedgerEntryRepository.GetByCustomerAsync(query.ActiveShopId, query.CustomerId, cancellationToken);
        var outstandingDue = await customerLedgerEntryRepository.GetCustomerBalanceAsync(query.ActiveShopId, query.CustomerId, cancellationToken);

        var ledgerChronological = ledgerEntries
            .OrderBy(e => e.EntryDate)
            .ThenBy(e => e.CreatedAt)
            .ToList();

        var runningBalance = 0m;
        var ledgerDtos = new List<CustomerLedgerEntryDto>(ledgerChronological.Count);
        foreach (var entry in ledgerChronological)
        {
            runningBalance += entry.EntryType == CustomerLedgerEntryType.PaymentReceived ? -entry.Amount : entry.Amount;
            ledgerDtos.Add(new CustomerLedgerEntryDto(
                entry.Id,
                entry.SaleId,
                entry.EntryType,
                entry.Amount,
                entry.EntryDate,
                entry.Notes,
                runningBalance));
        }

        var orderedLedgerDtos = ledgerDtos
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.EntryId)
            .ToList();

        var paymentHistory = orderedLedgerDtos
            .Where(e => e.EntryType == CustomerLedgerEntryType.PaymentReceived)
            .ToList();

        return new CustomerAccountDto(
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            outstandingDue,
            sales.Select(s => new CustomerAccountSaleDto(
                s.Id,
                s.InvoiceNumber,
                s.PaymentMethod,
                s.SoldAt,
                s.PaidAmount,
                s.DueAmount,
                s.TotalAmount)).ToList(),
            orderedLedgerDtos,
            paymentHistory);
    }
}
