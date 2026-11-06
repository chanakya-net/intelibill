using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomerCredit;

/// <summary>
/// The checkout's advisory credit preview. The posting decides again under the customer's lock,
/// so this answer can be stale by the time the sale is sent; its exposure is what an override
/// approves.
/// </summary>
public sealed class GetCustomerCreditQueryHandler(
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    public async Task<ErrorOr<CustomerCreditStatusDto>> HandleAsync(GetCustomerCreditQuery query, CancellationToken cancellationToken)
    {
        var customer = await customerRepository.GetByShopAndIdAsync(query.ShopId, query.CustomerId, cancellationToken);
        if (customer is null)
            return Errors.Customer.CustomerNotFound;

        var exposure = await customerLedgerEntryRepository.GetCustomerBalanceAsync(query.ShopId, query.CustomerId, cancellationToken);
        var state = CustomerCreditState.Of(customer);

        return new CustomerCreditStatusDto(
            customer.Id,
            customer.IsActive,
            customer.CreditPolicyMode,
            customer.CreditLimit,
            customer.CreditTermsDays,
            exposure,
            CustomerCreditPolicy.AvailableCredit(state, exposure));
    }
}
