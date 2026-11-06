using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Commands.ReversePayment;

/// <summary>
/// Reverses a whole payment receipt once: the amount is owed again through a linked ledger entry,
/// every allocation of the receipt stops counting (so each invoice or purchase receipt it settled
/// is outstanding again), a supplier payment's expense is voided, and the receipt stays unchanged.
/// </summary>
/// <remarks>
/// Owner only, with a reason and a required operation key: a retry replays the committed reversal,
/// and a second reversal of the same receipt answers <c>Payment.AlreadyReversed</c>.
/// </remarks>
public sealed class ReversePaymentCommandHandler(
    IUserRepository userRepository,
    IPaymentReceiptRepository receiptRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IExpenseRepository expenseRepository,
    IPaymentTransactionRunner transactionRunner,
    PaymentReceiptDtoBuilder dtoBuilder,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<PaymentReceiptDto>> HandleAsync(ReversePaymentCommand command, CancellationToken cancellationToken)
    {
        var role = await PaymentAuthorization.RoleAsync(userRepository, command.ActorUserId, command.ActiveShopId, cancellationToken);
        if (role.IsError)
            return role.Errors;
        if (role.Value != ShopRole.Owner)
            return Errors.Payment.UserIsNotOwner;

        var receipt = await receiptRepository.GetByShopAndIdAsync(command.ActiveShopId, command.ReceiptId, cancellationToken);
        if (receipt is null)
            return Errors.Payment.ReceiptNotFound;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey)
            ?? throw new InvalidOperationException("The validator requires an operation key.");
        var reason = command.Reason.Trim();

        return await transactionRunner.RunAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.PaymentReversal, operationKey),
            command.ActorUserId,
            receipt.Id,
            FinancialOperationHasher.Compute(new ReversalPayload(receipt.Id, reason)),
            ct => ReverseAsync(receipt, reason, command.ActorUserId, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<PaymentReceiptDto>> ReverseAsync(
        PaymentReceipt receipt,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        // Party first: every allocation of this receipt also takes it, so the allocations read next
        // are complete; then the documents they settled, in the same order an allocation locks them.
        var ids = new[] { receipt.Id };
        var partyLocked = PaymentAuthorization.LockOutcome(await receiptRepository.LockPartyAndTargetsAsync(
            receipt.ShopId, receipt.PartyType, receipt.PartyId, [], cancellationToken));
        if (partyLocked.IsError)
            return partyLocked.Errors;

        var allocations = await receiptRepository.GetAllocationsAsync(receipt.ShopId, ids, cancellationToken);
        var locked = PaymentAuthorization.LockOutcome(await receiptRepository.LockPartyAndTargetsAsync(
            receipt.ShopId,
            receipt.PartyType,
            receipt.PartyId,
            allocations.Select(a => a.TargetId).ToList(),
            cancellationToken));
        if (locked.IsError)
            return locked.Errors;

        if ((await receiptRepository.GetReversalsAsync(receipt.ShopId, ids, cancellationToken)).Count > 0)
            return Errors.Payment.AlreadyReversed;

        var businessDate = Intelibill.Domain.Common.BusinessCalendar.DateOf(DateTimeOffset.UtcNow);
        var notes = $"Reversal of payment {receipt.Id:N}: {reason}";
        if (notes.Length > 255)
            notes = notes[..255];

        Guid ledgerEntryId;
        if (receipt.PartyType == PaymentPartyType.Customer)
        {
            var entry = CustomerLedgerEntry.Create(
                receipt.ShopId, receipt.PartyId, saleId: null, CustomerLedgerEntryType.PaymentReversal,
                receipt.Amount, businessDate, notes, actorUserId);
            if (entry.IsError)
                return entry.Errors;

            await customerLedgerEntryRepository.AddAsync(entry.Value, cancellationToken);
            ledgerEntryId = entry.Value.Id;
        }
        else
        {
            var entry = SupplierLedgerEntry.Create(
                receipt.ShopId, receipt.PartyId, batchId: null, SupplierLedgerEntryType.Reversal,
                receipt.Amount, businessDate, notes, actorUserId);
            if (entry.IsError)
                return entry.Errors;

            await supplierLedgerEntryRepository.AddAsync(entry.Value, cancellationToken);
            ledgerEntryId = entry.Value.Id;

            if (receipt.ExpenseId is { } expenseId)
                await VoidExpenseChainAsync(receipt.ShopId, expenseId, cancellationToken);
        }

        var reversal = PaymentReversal.Create(receipt, reason, ledgerEntryId, businessDate, actorUserId);
        if (reversal.IsError)
            return reversal.Errors;

        await receiptRepository.AddReversalAsync(reversal.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await dtoBuilder.BuildAsync(receipt, cancellationToken);
    }

    /// <summary>
    /// The payment was also booked as an expense; it no longer happened. An expense correction voids
    /// the original and records a replacement, so the active expense may be further down the chain.
    /// </summary>
    private async Task VoidExpenseChainAsync(Guid shopId, Guid expenseId, CancellationToken cancellationToken)
    {
        var pending = new Queue<Guid>([expenseId]);
        var seen = new HashSet<Guid>();
        while (pending.TryDequeue(out var id))
        {
            if (!seen.Add(id))
                continue;

            if (await expenseRepository.GetByIdAsync(id, cancellationToken) is { } expense
                && expense.ShopId == shopId
                && !expense.IsVoided)
            {
                expense.MarkVoided();
                expenseRepository.Update(expense);
            }

            foreach (var correction in await expenseRepository.GetCorrectionsOfAsync(shopId, id, cancellationToken))
                pending.Enqueue(correction.Id);
        }
    }

    private sealed record ReversalPayload(Guid ReceiptId, string Reason);
}
