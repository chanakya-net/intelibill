using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Commands.AddCustomer;

public sealed class AddCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<CustomerDto>> HandleAsync(AddCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = Customer.Create(
            command.ShopId,
            command.Name,
            command.PhoneNumber,
            command.Address,
            command.IsActive);

        await customerRepository.AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CustomerDto(
            customer.Id,
            customer.Name,
            customer.PhoneNumber,
            customer.Address,
            customer.IsActive);
    }
}
