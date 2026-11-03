using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Gstin = Intelibill.Domain.Common.Gstin;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

public sealed class EditCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<CustomerDto>> HandleAsync(EditCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await customerRepository.GetByShopAndIdAsync(command.ShopId, command.CustomerId, cancellationToken);
        if (customer is null)
            return Errors.Customer.CustomerNotFound;

        // An absent GSTIN or state keeps what is recorded, so a client that predates the fields
        // cannot erase them; an empty value clears it. A new GSTIN without a state takes its own.
        var gstin = command.Gstin ?? customer.Gstin;
        var stateCode = command.StateCode ?? (command.Gstin is null ? customer.StateCode : null);
        if (Gstin.IsValid(Gstin.Normalize(gstin))
            && !string.IsNullOrWhiteSpace(stateCode)
            && !string.Equals(Gstin.StateCodeOf(Gstin.Normalize(gstin)!), stateCode.Trim(), StringComparison.Ordinal))
            return Errors.Customer.GstinStateMismatch;

        customer.Update(
            command.Name,
            command.PhoneNumber,
            command.Address,
            command.IsActive,
            command.CreditLimit,
            gstin,
            stateCode);

        customerRepository.Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerDto(
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            customer.Address,
            customer.IsActive,
            0m,
            customer.CreditLimit)
        {
            Gstin = customer.Gstin,
            StateCode = customer.StateCode,
        };
    }
}
