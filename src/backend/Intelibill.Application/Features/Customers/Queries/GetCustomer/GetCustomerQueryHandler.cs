using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Queries.GetCustomers;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomer;

public sealed class GetCustomerQueryHandler(ICustomerRepository customers, ICustomerLedgerEntryRepository ledger,
    ISaleRepository sales, IShopRepository shops, IBusinessClock clock)
{
    public async Task<ErrorOr<CustomerDto>> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByShopAndIdAsync(query.ShopId, query.CustomerId, cancellationToken);
        if (customer is null) return Errors.Customer.CustomerNotFound;
        var enriched = await new GetCustomersQueryHandler(customers, ledger, sales, shops, clock).EnrichAsync(query.ShopId, [customer], cancellationToken);
        return enriched.IsError ? enriched.Errors : enriched.Value[0].ToErrorOr();
    }
}
