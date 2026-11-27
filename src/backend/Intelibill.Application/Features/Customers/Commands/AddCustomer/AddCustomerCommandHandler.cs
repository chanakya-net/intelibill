using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Commands.AddCustomer;

public sealed class AddCustomerCommandHandler(
    ICustomerRepository customerRepository,
    DefinitionCreationTransaction creation,
    IUserRepository userRepository)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<CustomerDto>> HandleAsync(AddCustomerCommand command, CancellationToken cancellationToken)
    {
        var mayChangeCredit = await CustomerCreditSettingsAuthorization.CanChangeAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
        if (mayChangeCredit.IsError)
            return mayChangeCredit.Errors;

        // Staff cannot grant credit: their customers start with none until an owner or manager decides.
        var grantsCredit = command.CreditLimit > 0m
            || command.CreditTermsDays is not null
            || command.CreditPolicyMode != CreditPolicyMode.NoCredit;
        if (!mayChangeCredit.Value && grantsCredit)
            return Errors.CustomerCredit.SettingsChangeNotPermitted;

        return await creation.RunAsync<CustomerDto>(command.Session,
            (lease, ct) => CreateAsync(command, lease, ct), cancellationToken);
    }

    private async Task<ErrorOr<CustomerDto>> CreateAsync(
        AddCustomerCommand command, ShopLicenceLease lease, CancellationToken cancellationToken)
    {
        if (lease.Decision.EnforcedDenial() is { } denial)
            return denial;

        var customer = Customer.Create(
            command.ShopId,
            command.Name,
            command.PhoneNumber,
            command.Address,
            command.IsActive,
            command.CreditLimit,
            command.Gstin,
            command.StateCode);

        var policy = customer.SetCreditPolicy(command.CreditPolicyMode, command.CreditTermsDays);
        if (policy.IsError)
            return policy.Errors;

        await customerRepository.AddAsync(customer, cancellationToken);

        return CustomerDto.Saved(customer);
    }
}
