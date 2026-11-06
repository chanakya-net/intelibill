using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Commands.AllocatePayment;

/// <summary>
/// Applies more of a recorded receipt to the party's invoices or purchase receipts. Never moves an
/// existing allocation: a wrong allocation is corrected by reversing the receipt and recording it again.
/// </summary>
public sealed class AllocatePaymentCommandHandler(
    IUserRepository userRepository,
    IPaymentReceiptRepository receiptRepository,
    IPaymentAllocationPlanner allocationPlanner,
    IPaymentTransactionRunner transactionRunner,
    PaymentReceiptDtoBuilder dtoBuilder,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PaymentReceiptDto>> HandleAsync(AllocatePaymentCommand command, CancellationToken cancellationToken)
    {
        var authorized = await PaymentAuthorization.RequireOwnerOrManagerAsync(
            userRepository, command.ActorUserId, command.ActiveShopId, cancellationToken);
        if (authorized.IsError)
            return authorized.Errors;

        var receipt = await receiptRepository.GetByShopAndIdAsync(command.ActiveShopId, command.ReceiptId, cancellationToken);
        if (receipt is null)
            return Errors.Payment.ReceiptNotFound;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        var scope = operationKey is null
            ? null
            : new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.PaymentAllocation, operationKey);
        var requestHash = operationKey is null
            ? null
            : FinancialOperationHasher.Compute(new AllocationPayload(
                receipt.Id,
                command.Allocations
                    .OrderBy(a => a.TargetId)
                    .Select(a => new PaymentAllocationRequest(a.TargetId, FinancialOperationHasher.NormalizeAmount(a.Amount)))
                    .ToList()));

        return await transactionRunner.RunAsync<PaymentReceiptDto>(
            scope,
            command.ActorUserId,
            receipt.Id,
            requestHash,
            async ct =>
            {
                var locked = PaymentAuthorization.LockOutcome(await receiptRepository.LockPartyAndTargetsAsync(
                    receipt.ShopId,
                    receipt.PartyType,
                    receipt.PartyId,
                    command.Allocations.Select(a => a.TargetId).ToList(),
                    ct));
                if (locked.IsError)
                    return locked.Errors;

                // Read under the party lock: a reversal or another allocation of this receipt also takes it.
                var ids = new[] { receipt.Id };
                if ((await receiptRepository.GetReversalsAsync(receipt.ShopId, ids, ct)).Count > 0)
                    return Errors.Payment.ReversedReceiptCannotBeAllocated;

                var alreadyAllocated = (await receiptRepository.GetAllocationsAsync(receipt.ShopId, ids, ct)).Sum(a => a.Amount);
                var planned = await allocationPlanner.PlanAsync(receipt, alreadyAllocated, command.Allocations, command.ActorUserId, ct);
                if (planned.IsError)
                    return planned.Errors;

                foreach (var allocation in planned.Value)
                    await receiptRepository.AddAllocationAsync(allocation, ct);

                await unitOfWork.SaveChangesAsync(ct);
                return await dtoBuilder.BuildAsync(receipt, ct);
            },
            cancellationToken);
    }

    private sealed record AllocationPayload(Guid ReceiptId, IReadOnlyList<PaymentAllocationRequest> Allocations);
}
