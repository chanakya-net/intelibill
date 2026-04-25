using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
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

        customer.Update(
            command.Name,
            command.PhoneNumber,
            command.Address,
            command.IsActive);

        customerRepository.Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerDto(
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            customer.Address,
            customer.IsActive,
            0m);
    }
}
