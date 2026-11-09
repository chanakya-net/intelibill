using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Domain.ValueObjects;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Expenses.Commands.CorrectExpense;

public sealed class CorrectExpenseCommandHandler(
    IUserRepository userRepository,
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IUnitOfWork unitOfWork,
    IPaymentTransactionRunner transactionRunner,
    TillSourceSettlementService settlement)
{
    public async Task<ErrorOr<ExpenseDto>> HandleAsync(
        CorrectExpenseCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Expense.Forbidden;

        var original = await expenseRepository.GetByIdAsync(command.OriginalExpenseId, cancellationToken);
        if (original is null || original.ShopId != command.ShopId)
            return Errors.Expense.NotFound;

        if (command.Treatment.HasValue && (!Enum.IsDefined(command.Treatment.Value)
            || (command.Treatment == ExpenseTreatment.SupplierSettlement && original.Treatment != ExpenseTreatment.SupplierSettlement)
            || (original.Treatment == ExpenseTreatment.SupplierSettlement && command.Treatment != original.Treatment)))
            return Error.Validation("Expense.InvalidTreatment", "Supplier settlements retain their source treatment; other expenses require a valid treatment.");

        if (original.Treatment == ExpenseTreatment.SupplierSettlement || original.SupplierLedgerEntryId.HasValue)
            return Error.Validation("Expense.SupplierSettlementCorrectionRefused", "Correct supplier settlement through its payment receipt.");

        var key = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        return await transactionRunner.RunAsync<ExpenseDto>(key is null ? null : new(command.ShopId, FinancialOperationType.ExpenseCorrection, key),
            command.UserId, command.OriginalExpenseId, key is null ? null : ComputeRequestHash(command),
            ct => CorrectAsync(command, original, ct), cancellationToken);
    }

    private async Task<ErrorOr<ExpenseDto>> CorrectAsync(
        CorrectExpenseCommand command,
        Expense original,
        CancellationToken cancellationToken)
    {
        // The original is voided in its own month and the replacement lands in the new one: both
        // must be open, taken lowest month first.
        var physical = command.SettlementEvidence is { Disposition: not TillSettlementDisposition.NoPhysicalMovement };
        var postingDate = physical ? await settlement.TodayAsync(command.ShopId, cancellationToken) : command.ExpenseDate;
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ShopId, new[] { original.ExpenseDate, command.ExpenseDate, postingDate }.Distinct().Order().ToArray(), cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        if ((await settlement.IsEnabledAsync(command.ShopId, cancellationToken) || command.SettlementEvidence is not null) &&
            (command.Amount <= 0 || !TillMoney.IsValid(command.Amount)))
            return Error.Validation("Till.AmountInvalid", "Expense amount must be positive INR with at most two decimals.");
        var acquired = await settlement.AcquireAsync(command.ShopId, command.UserId, command.Till, command.OperationKey,
            command.SettlementEvidence, original.Amount - command.Amount, postingDate, cancellationToken);
        if (acquired.IsError) return acquired.Errors;
        var locked = await expenseRepository.LockForCorrectionAsync(command.ShopId, original.Id, cancellationToken);
        if (locked == RowLockOutcome.TimedOut) return Errors.Payment.Busy;
        if (locked == RowLockOutcome.NotFound) return Errors.Expense.NotFound;
        if (original.IsVoided) return Errors.Expense.AlreadyVoided;

        // Void original
        original.MarkVoided();
        expenseRepository.Update(original);

        // Upsert category
        var categoryName = command.CategoryName.Trim();
        var category = await expenseCategoryRepository.GetByNameAsync(command.ShopId, categoryName, cancellationToken);
        if (category is null)
        {
            category = ExpenseCategory.Create(command.ShopId, categoryName, DateTimeOffset.UtcNow);
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
        }

        // Create corrected expense
        var corrected = Expense.CreateCorrection(
            command.ShopId,
            category.Id,
            command.Amount,
            command.PaidTo,
            command.Description,
            command.ExpenseDate,
            command.UserId,
            command.OriginalExpenseId, command.Treatment ?? original.Treatment, original.SupplierLedgerEntryId, command.SettlementEvidence);

        var appended = await settlement.AppendAsync(acquired.Value, TillMovementSource.ExpenseCorrection, corrected.Id,
            command.UserId, command.OperationKey, command.SettlementEvidence, cancellationToken);
        if (appended.IsError) return appended.Errors;
        await expenseRepository.AddAsync(corrected, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(corrected, category.Name);
    }

    internal static string ComputeRequestHash(CorrectExpenseCommand command) => FinancialOperationHasher.Compute(new {
        command.UserId, command.OriginalExpenseId, CategoryName = command.CategoryName.Trim(), Amount = FinancialOperationHasher.NormalizeAmount(command.Amount),
        PaidTo = command.PaidTo.Trim(), Description = (string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim()),
        command.ExpenseDate, command.Treatment, command.Till, command.SettlementEvidence });

    private static ExpenseDto ToDto(Domain.Entities.Expense e, string categoryName) =>
        new(
            e.Id,
            e.ShopId,
            e.CategoryId,
            categoryName,
            e.Amount,
            e.PaidTo,
            e.Description,
            e.ExpenseDate,
            e.ActorUserId,
            e.IsVoided,
            e.OriginalExpenseId,
            e.SupplierLedgerEntryId,
            e.CreatedAt, e.Treatment, e.SettlementEvidence);
}
