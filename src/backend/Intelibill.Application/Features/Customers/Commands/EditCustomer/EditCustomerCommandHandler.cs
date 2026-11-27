using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

public sealed class EditCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork,
    IUserRepository userRepository,
    IShopLicenceFence licenceFence,
    ILiveSessionAuthority authority)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<CustomerDto>> HandleAsync(EditCustomerCommand command, CancellationToken cancellationToken)
    {
        return await PostingTransaction.RunAsync<CustomerDto>(unitOfWork,
            ct => EditAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<CustomerDto>> EditAsync(EditCustomerCommand command, CancellationToken cancellationToken)
    {
        // The definition already exists. Preserve the existing edit contract and serialize suspension.
        var licence = await licenceFence.EnterForSessionAsync(authority, command.Session, command.ActorUserId.GetValueOrDefault(),
            command.ShopId, LicenceAction.OperationalCapacity, cancellationToken);
        if (licence.IsError) return licence.Errors;
        if (licence.Value.SuspensionDenial() is { } denial) return denial;

        var customer = await customerRepository.GetByShopAndIdAsync(command.ShopId, command.CustomerId, cancellationToken);
        if (customer is null)
            return Errors.Customer.CustomerNotFound;

        // Reactivating a customer who may take credit reopens their credit, so it is a credit decision
        // too; deactivating only takes credit away and stays open to staff.
        var reactivatesCredit = command.IsActive && !customer.IsActive && command.CreditPolicyMode != CreditPolicyMode.NoCredit;
        var changesCredit = command.CreditLimit != customer.CreditLimit
            || command.CreditPolicyMode != customer.CreditPolicyMode
            || command.CreditTermsDays != customer.CreditTermsDays
            || reactivatesCredit;
        if (changesCredit)
        {
            var mayChangeCredit = await CustomerCreditSettingsAuthorization.CanChangeAsync(userRepository, command.ActorUserId, command.ShopId, cancellationToken);
            if (mayChangeCredit.IsError)
                return mayChangeCredit.Errors;
            if (!mayChangeCredit.Value)
                return Errors.CustomerCredit.SettingsChangeNotPermitted;
        }

        customer.Update(
            command.Name,
            command.PhoneNumber,
            command.Address,
            command.IsActive,
            command.CreditLimit,
            command.Gstin,
            command.StateCode);

        // The policy is checked against the new limit: lowering an enforced customer's limit to 0 is
        // refused rather than read as unlimited.
        var policy = customer.SetCreditPolicy(command.CreditPolicyMode, command.CreditTermsDays);
        if (policy.IsError)
            return policy.Errors;

        customerRepository.Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CustomerDto.Saved(customer);
    }
}
