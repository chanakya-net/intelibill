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
using Microsoft.Extensions.Logging;

namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

/// <summary>
/// Records money paid to a supplier: one <c>PAYMENT_MADE</c> ledger entry, its linked expense, the
/// <see cref="PaymentReceipt"/> that explains both and any purchase-receipt allocations, in one
/// transaction under the supplier → purchase receipt locks.
/// </summary>
public sealed partial class MakeSupplierPaymentCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository ledgerRepository,
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IPaymentReceiptRepository paymentReceiptRepository,
    IBankAccountRepository bankAccountRepository,
    IPaymentAllocationPlanner allocationPlanner,
    IPaymentTransactionRunner transactionRunner,
    IPostingPeriodPolicy postingPeriodPolicy,
    IUnitOfWork unitOfWork,
    ITillPostingService tillPosting,
    ITillSessionPolicy tillPolicy,
    IBusinessClock businessClock,
    ILogger<MakeSupplierPaymentCommandHandler> logger)
{
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
        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (operationKey is null)
        {
            LogUnkeyedPayment(command.ActiveShopId);
            return await transactionRunner.RunAsync<SupplierLedgerEntryDto>(
                scope: null, command.ActorUserId, command.SupplierId, requestHash: null,
                ct => PostAsync(command, allocations, ct), cancellationToken);
        }

        return await transactionRunner.RunAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.SupplierPayment, operationKey),
            command.ActorUserId,
            command.SupplierId,
            ComputeRequestHash(command, allocations),
            ct => PostAsync(command, allocations, ct),
            cancellationToken);
    }

    /// <summary>A request without receipt details hashes exactly as before receipts existed.</summary>
    internal static string ComputeRequestHash(MakeSupplierPaymentCommand command, IReadOnlyList<PaymentAllocationRequest> allocations)
    {
        if (command.Till is not null)
            return FinancialOperationHasher.Compute(new { LegacyHash = ComputeRequestHash(command with { Till = null }, allocations), command.ActorUserId, command.Till });
        var notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        var amount = FinancialOperationHasher.NormalizeAmount(command.Amount);
        var reference = string.IsNullOrWhiteSpace(command.ExternalReference) ? null : command.ExternalReference.Trim();
        if (command.Method is null && command.BankAccountId is null && reference is null && allocations.Count == 0)
            return FinancialOperationHasher.Compute(new SupplierPaymentPayload(command.SupplierId, amount, command.PaymentDate, notes));

        return FinancialOperationHasher.Compute(new SupplierReceiptPayload(
            command.SupplierId,
            amount,
            command.PaymentDate,
            notes,
            command.Method,
            command.BankAccountId,
            reference,
            allocations
                .OrderBy(a => a.TargetId)
                .Select(a => new AllocationPayload(a.TargetId, FinancialOperationHasher.NormalizeAmount(a.Amount)))
                .ToList()));
    }

    private async Task<ErrorOr<SupplierLedgerEntryDto>> PostAsync(
        MakeSupplierPaymentCommand command,
        IReadOnlyList<PaymentAllocationRequest> allocationRequests,
        CancellationToken cancellationToken)
    {
        // The payment is dated by the caller; its month must be open, and stays so until this commits.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ActiveShopId, command.PaymentDate, cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        if (tillPolicy.IsEnabled(command.ActiveShopId))
        {
            if (FinancialOperationHasher.NormalizeKey(command.OperationKey) is null) return Errors.Operation.InvalidKey;
            if (command.Method is null or ReceiptTenderMethod.UnknownRecordedMethod) return Errors.Till.TenderRequired;
        }
        var acquired = await tillPosting.AcquireAsync(command.ActiveShopId, command.ActorUserId, command.Till, command.PaymentDate, cancellationToken);
        if (acquired.IsError) return acquired.Errors;

        var lockOutcome = await paymentReceiptRepository.LockPartyAndTargetsAsync(
            command.ActiveShopId,
            PaymentPartyType.Supplier,
            command.SupplierId,
            allocationRequests.Select(a => a.TargetId).ToList(),
            cancellationToken);
        switch (lockOutcome)
        {
            case RowLockOutcome.Acquired:
                break;
            case RowLockOutcome.NotFound:
                return Errors.Supplier.SupplierNotFound;
            case RowLockOutcome.TimedOut:
                return Errors.Payment.Busy;
            default:
                throw new InvalidOperationException($"Unknown payment lock outcome '{lockOutcome}'.");
        }

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

        const string categoryName = "Supplier Payments";
        var category = await expenseCategoryRepository.GetByNameAsync(command.ActiveShopId, categoryName, cancellationToken);
        if (category is null)
        {
            category = ExpenseCategory.Create(command.ActiveShopId, categoryName, DateTimeOffset.UtcNow);
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
        }

        var entry = entryOrError.Value;
        var expense = Expense.CreateFromSupplierPayment(
            command.ActiveShopId,
            category.Id,
            command.Amount,
            "Supplier",
            command.Notes,
            command.PaymentDate,
            command.ActorUserId,
            entry.Id);

        var receiptResult = PaymentReceipt.Record(
            command.ActiveShopId,
            PaymentPartyType.Supplier,
            command.SupplierId,
            command.Amount,
            command.Method ?? ReceiptTenderMethod.UnknownRecordedMethod,
            command.BankAccountId,
            command.ExternalReference,
            command.PaymentDate,
            DateTimeOffset.UtcNow,
            command.Notes,
            entry.Id,
            expense.Id,
            command.ActorUserId);
        if (receiptResult.IsError)
            return receiptResult.Errors;

        var receipt = receiptResult.Value;
        var allocations = await allocationPlanner.PlanAsync(receipt, 0m, allocationRequests, command.ActorUserId, cancellationToken);
        if (allocations.IsError)
            return allocations.Errors;

        await ledgerRepository.AddAsync(entry, cancellationToken);
        await expenseRepository.AddAsync(expense, cancellationToken);
        await paymentReceiptRepository.AddAsync(receipt, cancellationToken);
        foreach (var allocation in allocations.Value)
            await paymentReceiptRepository.AddAllocationAsync(allocation, cancellationToken);

        if (acquired.Value is { } session)
        {
            var appended = await tillPosting.AppendAsync(session, new(TillMovementSource.SupplierPayment, receipt.Id, "payment",
                [new TenderLine(receipt.Method, receipt.Amount, null, null, receipt.ExternalReference, receipt.BankAccountId)], -1m,
                businessClock.UtcNow, command.OperationKey!.Trim(), command.Notes), command.ActorUserId, cancellationToken);
            if (appended.IsError) return appended.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var e = entry;
        return new SupplierLedgerEntryDto(e.Id, e.SupplierId, e.EntryType, e.Amount, e.EntryDate, e.Notes, receipt.Id);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Legacy unkeyed financial command: supplier payment made without an operation key in shop {ShopId}.")]
    private partial void LogUnkeyedPayment(Guid shopId);

    private sealed record SupplierPaymentPayload(Guid SupplierId, decimal Amount, DateOnly PaymentDate, string? Notes);

    private sealed record SupplierReceiptPayload(
        Guid SupplierId,
        decimal Amount,
        DateOnly PaymentDate,
        string? Notes,
        ReceiptTenderMethod? Method,
        Guid? BankAccountId,
        string? ExternalReference,
        IReadOnlyList<AllocationPayload> Allocations);

    private sealed record AllocationPayload(Guid TargetId, decimal Amount);
}
