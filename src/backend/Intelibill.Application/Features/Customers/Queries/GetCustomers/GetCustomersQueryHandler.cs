using ErrorOr;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomers;

public sealed class GetCustomersQueryHandler(
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    public async Task<ErrorOr<IReadOnlyList<CustomerDto>>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        var customers = await customerRepository.GetByOwnerUserIdAsync(query.OwnerUserId, cancellationToken);
        var customerIds = customers.Select(c => c.Id).ToList();
        var balances = await customerLedgerEntryRepository.GetCustomerBalancesAsync(
            query.ActiveShopId,
            customerIds,
            cancellationToken);

        return customers.Select(c => new CustomerDto(
            c.Id,
            c.Name,
            c.PhoneNumber,
            c.Address,
            c.IsActive,
            balances.TryGetValue(c.Id, out var due) ? due : 0m)).ToList();
    }
}
