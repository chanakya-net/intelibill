using ErrorOr;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomers;

public sealed class GetCustomersQueryHandler(
    ICustomerRepository customerRepository)
{
    public async Task<ErrorOr<IReadOnlyList<CustomerDto>>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        var customers = await customerRepository.GetByOwnerUserIdAsync(query.OwnerUserId, cancellationToken);

        return customers.Select(c => new CustomerDto(
            c.Id,
            c.Name,
            c.PhoneNumber,
            c.Address,
            c.IsActive)).ToList();
    }
}
