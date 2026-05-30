using ErrorOr;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomers;

public sealed class GetCustomersQueryHandler(
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ISaleRepository saleRepository)
{
    public async Task<ErrorOr<IReadOnlyList<CustomerDto>>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        var customers = await customerRepository.GetByShopIdAsync(query.ShopId, cancellationToken);
        var customerIds = customers.Select(c => c.Id).ToList();
        var balances = await customerLedgerEntryRepository.GetCustomerBalancesAsync(
            query.ShopId,
            customerIds,
            cancellationToken);

        var todayUtc = DateTimeOffset.UtcNow.UtcDateTime;
        var monthStartUtc = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStartUtc = monthStartUtc.AddMonths(1);

        var metrics = await saleRepository.GetCustomerSalesMetricsAsync(
            query.ShopId,
            customerIds,
            monthStartUtc,
            nextMonthStartUtc,
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
                m?.CurrentMonthRevenue ?? 0m);
        }).ToList();
    }
}
