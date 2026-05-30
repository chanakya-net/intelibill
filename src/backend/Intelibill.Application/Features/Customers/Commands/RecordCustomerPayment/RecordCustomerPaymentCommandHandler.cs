using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;

public sealed class RecordCustomerPaymentCommandHandler(
    IUserRepository userRepository,
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<CustomerLedgerEntryDto>> HandleAsync(
        RecordCustomerPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Customer.UserIsNotOwnerOrManager;

        var customer = await customerRepository.GetByShopAndIdAsync(command.ActiveShopId, command.CustomerId, cancellationToken);
        if (customer is null)
            return Errors.Customer.CustomerNotFound;

        var paymentEntryResult = CustomerLedgerEntry.Create(
            command.ActiveShopId,
            command.CustomerId,
            saleId: null,
            CustomerLedgerEntryType.PaymentReceived,
            command.Amount,
            command.PaymentDate,
            command.Notes,
            command.ActorUserId);

        if (paymentEntryResult.IsError)
            return paymentEntryResult.Errors;

        var paymentEntry = paymentEntryResult.Value;
        await customerLedgerEntryRepository.AddAsync(paymentEntry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var balance = await customerLedgerEntryRepository.GetCustomerBalanceAsync(command.ActiveShopId, command.CustomerId, cancellationToken);

        return new CustomerLedgerEntryDto(
            paymentEntry.Id,
            paymentEntry.SaleId,
            paymentEntry.EntryType,
            paymentEntry.Amount,
            paymentEntry.EntryDate,
            paymentEntry.Notes,
            balance);
    }
}
