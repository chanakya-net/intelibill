using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Locks the sale's customer and decides whether its new due is allowed, overridden or refused.</summary>
public sealed class SaleCreditDecider(
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IUserRepository userRepository,
    SaleReplayReader replayReader)
{
    internal sealed record CustomerCreditCheck(CreditDecision Decision, int? CreditTermsDays, ErrorOr<SaleDto>? Replay = null);

    /// <summary>
    /// Locks the customer, then judges the new due on what is committed now. A retry of a sale that
    /// committed while this request waited for the lock replays it instead of being judged again.
    /// </summary>
    internal async Task<ErrorOr<CustomerCreditCheck>> LockCustomerAndDecideCreditAsync(
        RecordSaleCommand command,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var lockOutcome = await customerRepository.LockForCreditAsync(command.ShopId, customerId, cancellationToken);
        switch (lockOutcome)
        {
            case RowLockOutcome.Acquired:
                break;
            case RowLockOutcome.TimedOut:
                return Errors.CustomerCredit.Busy;
            default:
                return Errors.Sale.CreditCustomerNotFound;
        }

        var normalizedKey = command.IdempotencyKey.Trim();
        var replay = await replayReader.TryGetReplayForIdempotentSaleAsync(
            command,
            normalizedKey,
            RecordSaleIdempotencyHasher.ComputeHash(command),
            cancellationToken);
        if (replay.HasValue)
            return new CustomerCreditCheck(new CreditDecision(CreditDecisionKind.NoNewDue, 0m, 0m, 0m), null, replay.Value);

        var customer = await customerRepository.GetCurrentAsync(command.ShopId, customerId, cancellationToken);
        if (customer is null)
            return Errors.Sale.CreditCustomerNotFound;

        var exposure = await customerLedgerEntryRepository.GetCustomerBalanceAsync(command.ShopId, customerId, cancellationToken);

        ShopRole? actorRole = null;
        if (command.CreditOverride is not null)
        {
            var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
            actorRole = actor?.ShopMemberships.FirstOrDefault(m => m.ShopId == command.ShopId)?.Role;
        }

        var decision = CustomerCreditPolicy.Evaluate(
            CustomerCreditState.Of(customer),
            exposure,
            command.DueAmount,
            command.CreditOverride,
            actorRole);
        if (decision.IsError)
            return decision.Errors;

        return new CustomerCreditCheck(decision.Value, customer.CreditTermsDays);
    }
}
