using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Domain.ValueObjects;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Common.Time;
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
    IUnitOfWork unitOfWork,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    TillSourceSettlementService settlement,
    IShopLicenceFence licenceFence,
    PaymentCompletionPolicy completionPolicy)
{
    [LicenceAction(LicenceAction.ExistingPayment)]
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
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.PaymentReversal, operationKey)
            { SuspensionAction = LicenceAction.ExistingPayment },
            command.ActorUserId,
            receipt.Id,
            FinancialOperationHasher.Compute(new ReversalPayload(receipt.Id, reason, command.Till, command.SettlementEvidence)),
            ct => ReverseAsync(receipt, reason, command, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<PaymentReceiptDto>> ReverseAsync(
        PaymentReceipt receipt,
        string reason,
        ReversePaymentCommand command,
        CancellationToken cancellationToken)
    {
        var licence = await licenceFence.EnterAsync(receipt.ShopId, LicenceAction.ExistingPayment, cancellationToken);
        if (licence.IsError) return licence.Errors;
        if (PaymentCompletionPolicy.Denial(licence.Value.CurrentDecision()) is { } denied) return denied;

        var actorUserId = command.ActorUserId;
        var shop = await shopRepository.GetByIdAsync(receipt.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        // The reversal posts today, never into the month of the payment it undoes. A supplier
        // payment's reversal also voids its expense (which can never be corrected on its own), and
        // that changes the month the expense sits in: that month must be open too.
        var businessDate = businessClock.Today(shop.BusinessTimeZoneId);
        var expensesToVoid = receipt.PartyType == PaymentPartyType.Supplier && receipt.ExpenseId is { } expenseId
            && await expenseRepository.GetByIdAsync(expenseId, cancellationToken) is { IsVoided: false } linkedExpense
            && linkedExpense.ShopId == receipt.ShopId
            ? [linkedExpense]
            : new List<Expense>();
        List<Error>? fenceErrors;
        if (expensesToVoid.Count == 0)
        {
            var single = await postingPeriodPolicy.AuthorizeForWriteAsync(receipt.ShopId, businessDate, cancellationToken);
            fenceErrors = single.IsError ? single.Errors : null;
        }
        else
        {
            var several = await postingPeriodPolicy.AuthorizeForWriteAsync(
                receipt.ShopId,
                [businessDate, .. expensesToVoid.Select(e => e.ExpenseDate)],
                cancellationToken);
            fenceErrors = several.IsError ? several.Errors : null;
        }

        if (fenceErrors is not null)
            return fenceErrors;

        var acquired = await settlement.AcquireAsync(receipt.ShopId, actorUserId, command.Till, command.OperationKey,
            command.SettlementEvidence, receipt.PartyType == PaymentPartyType.Customer ? -receipt.Amount : receipt.Amount,
            businessDate, cancellationToken);
        if (acquired.IsError) return acquired.Errors;

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

        var permitted = await completionPolicy.CheckReceiptAsync(licence.Value, receipt, cancellationToken);
        if (permitted.IsError) return permitted.Errors;

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

            foreach (var expense in expensesToVoid)
            {
                expense.MarkVoided();
                expenseRepository.Update(expense);
            }
        }

        var reversal = PaymentReversal.Create(receipt, reason, ledgerEntryId, businessDate, actorUserId, command.SettlementEvidence);
        if (reversal.IsError)
            return reversal.Errors;

        var appended = await settlement.AppendAsync(acquired.Value,
            receipt.PartyType == PaymentPartyType.Customer ? TillMovementSource.CustomerPaymentReversal : TillMovementSource.SupplierPaymentReversal,
            reversal.Value.Id, actorUserId, command.OperationKey, command.SettlementEvidence, cancellationToken);
        if (appended.IsError) return appended.Errors;
        await receiptRepository.AddReversalAsync(reversal.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await dtoBuilder.BuildAsync(receipt, cancellationToken);
    }

    private sealed record ReversalPayload(Guid ReceiptId, string Reason, TillPostingContext? Till, TillSettlementEvidence? SettlementEvidence);
}
