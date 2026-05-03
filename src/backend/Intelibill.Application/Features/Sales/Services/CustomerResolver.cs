using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Application.Features.Sales.Commands.RecordSale;

namespace Intelibill.Application.Features.Sales.Services;

internal sealed class CustomerResolver(
    ICustomerRepository customerRepository) : ICustomerResolver
{
    public async Task<ErrorOr<Customer?>> ResolveAsync(
        Guid shopId,
        Guid? customerId,
        string? customerPhone,
        bool hasDueAmount,
        PaymentMethod paymentMethod,
        CancellationToken cancellationToken)
    {
        var shouldResolve = customerId.HasValue || hasDueAmount || paymentMethod == PaymentMethod.Credit;
        if (!shouldResolve)
            return (Customer?)null;

        Customer? resolvedCustomer = null;
        var normalizedPhone = string.IsNullOrWhiteSpace(customerPhone) ? null : customerPhone.Trim();

        if (customerId.HasValue)
        {
            resolvedCustomer = await customerRepository.GetByShopAndIdAsync(shopId, customerId.Value, cancellationToken);
            if (resolvedCustomer is null)
                return Errors.Sale.CreditCustomerNotFound;
        }

        if (!string.IsNullOrWhiteSpace(normalizedPhone))
        {
            var customerByPhone = await customerRepository.GetByShopAndPhoneAsync(shopId, normalizedPhone, cancellationToken);
            if (resolvedCustomer is null)
            {
                resolvedCustomer = customerByPhone;
            }
            else if (customerByPhone is not null && customerByPhone.Id != resolvedCustomer.Id)
            {
                return Errors.Sale.CustomerIdentityMismatch;
            }
        }

        if ((hasDueAmount || paymentMethod == PaymentMethod.Credit) && resolvedCustomer is null)
            return Errors.Sale.CreditCustomerNotFound;

        return resolvedCustomer;
    }
}
