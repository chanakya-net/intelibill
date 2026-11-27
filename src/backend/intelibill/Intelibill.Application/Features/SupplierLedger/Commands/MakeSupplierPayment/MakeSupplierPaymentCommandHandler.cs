using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.ValueObjects;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

/// <summary>
/// Records money paid to a supplier: one <c>PAYMENT_MADE</c> ledger entry, its linked expense, the
/// <see cref="PaymentReceipt"/> that explains both and any purchase-receipt allocations, in one
/// transaction under the supplier → purchase receipt locks.
/// </summary>
public sealed class MakeSupplierPaymentCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository ledgerRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IBankAccountRepository bankAccountRepository,
    IPaymentTransactionRunner transactionRunner,
    IPostingPeriodPolicy postingPeriodPolicy,
    IUnitOfWork unitOfWork,
    ITillPostingService tillPosting,
    IShopLicenceFence licenceFence,
    PaymentCompletionPolicy completionPolicy,
    PaymentReceiptPoster receiptPoster,
    SupplierPaymentExpensePoster expensePoster)
{
    [LicenceAction(LicenceAction.DueSettlement)]
    public async Task<ErrorOr<SupplierLedgerEntryDto>> HandleAsync(
        MakeSupplierPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null || !actorMembership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Supplier.UserIsNotOwnerOrManager;

        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        if (command.BankAccountId is { } bankAccountId)
        {
            var bankAccount = await bankAccountRepository.GetByIdAsync(bankAccountId, cancellationToken);
            if (bankAccount is null || bankAccount.ShopId != command.ActiveShopId)
                return Errors.Payment.BankAccountNotFound;
        }

        var allocations = command.Allocations ?? [];
        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey)
            ?? throw new InvalidOperationException("The validator requires an operation key.");

        return await transactionRunner.RunAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.SupplierPayment, operationKey)
            { SuspensionAction = LicenceAction.DueSettlement },
            command.ActorUserId,
            command.SupplierId,
            ComputeRequestHash(command, allocations),
            ct => PostAsync(command, allocations, ct),
            cancellationToken);
    }

    /// <summary>
    /// The payment's business fields and till context, never the actor: another owner or manager
    /// resending the same key reconciles the same payment.
    /// </summary>
    internal static string ComputeRequestHash(MakeSupplierPaymentCommand command, IReadOnlyList<PaymentAllocationRequest> allocations) =>
        FinancialOperationHasher.Compute(new SupplierPaymentPayload(
            command.SupplierId,
            FinancialOperationHasher.NormalizeAmount(command.Amount),
            command.PaymentDate,
            string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
            command.Method,
            command.BankAccountId,
            string.IsNullOrWhiteSpace(command.ExternalReference) ? null : command.ExternalReference.Trim(),
            allocations
                .OrderBy(a => a.TargetId)
                .Select(a => new AllocationPayload(a.TargetId, FinancialOperationHasher.NormalizeAmount(a.Amount)))
                .ToList(),
            command.Till));

    private async Task<ErrorOr<SupplierLedgerEntryDto>> PostAsync(
        MakeSupplierPaymentCommand command,
        IReadOnlyList<PaymentAllocationRequest> allocationRequests,
        CancellationToken cancellationToken)
    {
        var licence = await licenceFence.EnterAsync(command.ActiveShopId, LicenceAction.DueSettlement, cancellationToken);
        if (licence.IsError) return licence.Errors;
        if (PaymentCompletionPolicy.Denial(licence.Value.CurrentDecision()) is { } denied) return denied;

        // The payment is dated by the caller; its month must be open, and stays so until this commits.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ActiveShopId, command.PaymentDate, cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        var acquired = await AcquireTillSessionAsync(command, cancellationToken);
        if (acquired.IsError) return acquired.Errors;

        var locked = await LockSupplierAndTargetsAsync(command, allocationRequests, cancellationToken);
        if (locked.IsError)
            return locked.Errors;

        var entryOrError = SupplierLedgerEntry.Create(
            command.ActiveShopId,
            command.SupplierId,
            batchId: null,
            SupplierLedgerEntryType.PaymentMade,
            amount: command.Amount,
            command.PaymentDate,
            command.Notes,
            command.ActorUserId);

        if (entryOrError.IsError)
            return entryOrError.Errors;

        var (category, newCategory) = await expensePoster.ResolveExpenseCategoryAsync(command.ActiveShopId, cancellationToken);

        var entry = entryOrError.Value;
        var expense = SupplierPaymentExpensePoster.CreateExpense(
            command.ActiveShopId,
            category.Id,
            command.Amount,
            command.Notes,
            command.PaymentDate,
            command.ActorUserId,
            entry.Id);

        var receiptResult = RecordReceipt(command, entry, expense);
        if (receiptResult.IsError)
            return receiptResult.Errors;

        var receipt = receiptResult.Value;
        var allocations = await receiptPoster.PlanAllocationsAsync(receipt, allocationRequests, command.ActorUserId, cancellationToken);
        if (allocations.IsError)
            return allocations.Errors;

        var permitted = await completionPolicy.CheckSettlementAsync(licence.Value, receipt, allocationRequests, cancellationToken);
        if (permitted.IsError) return permitted.Errors;

        await StagePaymentAsync(category, newCategory, entry, expense, receipt, allocations.Value, cancellationToken);

        if (acquired.Value is { } session)
        {
            var appended = await AppendTillMovementAsync(session, command, receipt, cancellationToken);
            if (appended.IsError) return appended.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var e = entry;
        return new SupplierLedgerEntryDto(e.Id, e.SupplierId, e.EntryType, e.Amount, e.EntryDate, e.Notes, receipt.Id);
    }

    private async Task<ErrorOr<TillSession?>> AcquireTillSessionAsync(
        MakeSupplierPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var gate = await tillPosting.GateAsync(command.ActiveShopId, cancellationToken);
        if (gate.IsError) return gate.Errors;
        if (gate.Value && command.Method is (null or ReceiptTenderMethod.UnknownRecordedMethod))
            return Errors.Till.TenderRequired;
        return await tillPosting.AcquireAsync(
            command.ActiveShopId,
            command.ActorUserId,
            command.Till,
            command.PaymentDate,
            cancellationToken);
    }

    private async Task<ErrorOr<Success>> LockSupplierAndTargetsAsync(
        MakeSupplierPaymentCommand command,
        IReadOnlyList<PaymentAllocationRequest> allocationRequests,
        CancellationToken cancellationToken)
    {
        var lockOutcome = await paymentReceiptRepository.LockPartyAndTargetsAsync(
            command.ActiveShopId,
            PaymentPartyType.Supplier,
            command.SupplierId,
            allocationRequests.Select(a => a.TargetId).ToList(),
            cancellationToken);
        switch (lockOutcome)
        {
            case RowLockOutcome.Acquired:
                return Result.Success;
            case RowLockOutcome.NotFound:
                return Errors.Supplier.SupplierNotFound;
            case RowLockOutcome.TimedOut:
                return Errors.Payment.Busy;
            default:
                throw new InvalidOperationException($"Unknown payment lock outcome '{lockOutcome}'.");
        }
    }

    private ErrorOr<PaymentReceipt> RecordReceipt(
        MakeSupplierPaymentCommand command,
        SupplierLedgerEntry entry,
        Expense expense) =>
        receiptPoster.RecordReceipt(
            command.ActiveShopId,
            PaymentPartyType.Supplier,
            command.SupplierId,
            command.Amount,
            command.Method,
            command.BankAccountId,
            command.ExternalReference,
            command.PaymentDate,
            command.Notes,
            entry.Id,
            expense.Id,
            command.ActorUserId);

    private async Task StagePaymentAsync(
        ExpenseCategory category,
        bool newCategory,
        SupplierLedgerEntry entry,
        Expense expense,
        PaymentReceipt receipt,
        IReadOnlyList<PaymentAllocation> allocations,
        CancellationToken cancellationToken)
    {
        await expensePoster.StageCategoryAsync(category, newCategory, cancellationToken);
        await ledgerRepository.AddAsync(entry, cancellationToken);
        await expensePoster.StageExpenseAsync(expense, cancellationToken);
        await receiptPoster.StageReceiptAsync(receipt, allocations, cancellationToken);
    }

    private async Task<ErrorOr<Success>> AppendTillMovementAsync(
        TillSession session,
        MakeSupplierPaymentCommand command,
        PaymentReceipt receipt,
        CancellationToken cancellationToken) =>
        await receiptPoster.AppendTillMovementAsync(
            session,
            TillMovementSource.SupplierPayment,
            -1m,
            receipt,
            command.OperationKey,
            command.Notes,
            command.ActorUserId,
            cancellationToken);

    private sealed record SupplierPaymentPayload(
        Guid SupplierId,
        decimal Amount,
        DateOnly PaymentDate,
        string? Notes,
        ReceiptTenderMethod? Method,
        Guid? BankAccountId,
        string? ExternalReference,
        IReadOnlyList<AllocationPayload> Allocations,
        TillPostingContext? Till);

    private sealed record AllocationPayload(Guid TargetId, decimal Amount);
}
