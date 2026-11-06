using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Services;
using Gstin = Intelibill.Domain.Common.Gstin;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

public sealed class EditCustomerCommandHandler(
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork,
    IUserRepository userRepository)
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

        // An absent policy keeps the stored policy and terms, so a client that predates them
        // cannot erase them. The policy is checked against the new limit: lowering an enforced
        // customer's limit to 0 is refused rather than read as unlimited.
        var mode = command.CreditPolicyMode ?? customer.CreditPolicyMode;
        var termsDays = command.CreditPolicyMode is null ? customer.CreditTermsDays : command.CreditTermsDays;
        // Reactivating a customer who may take credit reopens their credit, so it is a credit decision
        // too; deactivating only takes credit away and stays open to staff.
        var reactivatesCredit = command.IsActive && !customer.IsActive && mode != CreditPolicyMode.NoCredit;
        var changesCredit = command.CreditLimit != customer.CreditLimit
            || mode != customer.CreditPolicyMode
            || termsDays != customer.CreditTermsDays
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
            gstin,
            stateCode);

        var policy = customer.SetCreditPolicy(mode, termsDays);
        if (policy.IsError)
            return policy.Errors;

        customerRepository.Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CustomerDto.Saved(customer);
    }
}
