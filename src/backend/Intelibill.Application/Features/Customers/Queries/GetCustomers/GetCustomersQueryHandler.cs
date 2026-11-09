using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomers;

public sealed class GetCustomersQueryHandler(
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ISaleRepository saleRepository,
    IShopRepository shopRepository,
    IBusinessClock businessClock)
{
    public async Task<ErrorOr<IReadOnlyList<CustomerDto>>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        var customers = await customerRepository.GetByShopIdAsync(query.ShopId, cancellationToken);
        return await EnrichAsync(query.ShopId, customers, cancellationToken);
    }

    internal async Task<ErrorOr<IReadOnlyList<CustomerDto>>> EnrichAsync(Guid shopId, IReadOnlyList<Intelibill.Domain.Entities.Customer> customers, CancellationToken cancellationToken)
    {
        var customerIds = customers.Select(c => c.Id).ToList();
        var balances = await customerLedgerEntryRepository.GetCustomerBalancesAsync(
            shopId,
            customerIds,
            cancellationToken);

        var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        // The month the shop is in now, read on its own calendar.
        var today = businessClock.Today(shop.BusinessTimeZoneId);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var month = businessClock.GetUtcWindow(monthStart, monthStart.AddMonths(1).AddDays(-1), shop.BusinessTimeZoneId);

        var metrics = await saleRepository.GetCustomerSalesMetricsAsync(
            shopId,
            customerIds,
            month,
            cancellationToken);

        return customers.Select(c =>
        {
            metrics.TryGetValue(c.Id, out var m);
            return new CustomerDto(
                c.Id,
                c.Name,
                c.PhoneNumber,
                c.Address,
                c.IsActive,
                balances.TryGetValue(c.Id, out var due) ? due : 0m,
                c.CreditLimit,
                m?.PurchaseCount ?? 0,
                m?.LifetimeRevenue ?? 0m,
                m?.CurrentMonthRevenue ?? 0m)
            {
                Gstin = c.Gstin,
                StateCode = c.StateCode,
                CreditPolicyMode = c.CreditPolicyMode,
                CreditTermsDays = c.CreditTermsDays,
            };
        }).ToList();
    }
}
