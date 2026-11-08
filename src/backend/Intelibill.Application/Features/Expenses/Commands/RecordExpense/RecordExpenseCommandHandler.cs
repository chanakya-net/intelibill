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

namespace Intelibill.Application.Features.Expenses.Commands.RecordExpense;

public sealed class RecordExpenseCommandHandler(
    IUserRepository userRepository,
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IUnitOfWork unitOfWork,
    IPaymentTransactionRunner transactionRunner,
    TillSourceSettlementService settlement)
{
    public async Task<ErrorOr<ExpenseDto>> HandleAsync(
        RecordExpenseCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Treatment) || command.Treatment == ExpenseTreatment.SupplierSettlement)
            return Error.Validation("Expense.InvalidTreatment", "Select Unclassified, Operating or CapitalOrOtherExcluded.");

        var actor = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Expense.Forbidden;

        var key = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        return await transactionRunner.RunAsync<ExpenseDto>(key is null ? null : new(command.ShopId, FinancialOperationType.ExpenseRecord, key),
            command.UserId, command.ShopId, key is null ? null : ComputeRequestHash(command), ct => PostAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<ExpenseDto>> PostAsync(RecordExpenseCommand command, CancellationToken cancellationToken)
    {
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ShopId, command.ExpenseDate, cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        if (command.TenderLine is not null && command.SettlementEvidence is not null)
            return Error.Validation("Till.SettlementEvidenceInvalid", "Specify one expense settlement contract.");
        var evidence = command.SettlementEvidence ?? (command.TenderLine is null ? null :
            new TillSettlementEvidence(TillSettlementDisposition.MoneyPaid, command.TenderLine, "Expense payment"));
        if ((settlement.IsEnabled(command.ShopId) || command.SettlementEvidence is not null || command.TenderLine is not null) &&
            (command.Amount <= 0 || !TillMoney.IsValid(command.Amount)))
            return Error.Validation("Till.AmountInvalid", "Expense amount must be positive INR with at most two decimals.");
        var acquired = await settlement.AcquireAsync(command.ShopId, command.UserId, command.Till, command.OperationKey,
            evidence, -command.Amount, command.ExpenseDate, cancellationToken);
        if (acquired.IsError) return acquired.Errors;

        // Upsert category
        var categoryName = command.CategoryName.Trim();
        var category = await expenseCategoryRepository.GetByNameAsync(command.ShopId, categoryName, cancellationToken);
        if (category is null)
        {
            category = ExpenseCategory.Create(command.ShopId, categoryName, DateTimeOffset.UtcNow);
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
        }

        var expense = Expense.Create(
            command.ShopId,
            category.Id,
            command.Amount,
            command.PaidTo,
            command.Description,
            command.ExpenseDate,
            command.UserId, command.Treatment, evidence);

        var appended = await settlement.AppendAsync(acquired.Value, TillMovementSource.Expense, expense.Id,
            command.UserId, command.OperationKey, evidence, cancellationToken);
        if (appended.IsError) return appended.Errors;
        await expenseRepository.AddAsync(expense, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(expense, category.Name);
    }

    internal static string ComputeRequestHash(RecordExpenseCommand command) => FinancialOperationHasher.Compute(new {
        command.UserId, CategoryName = command.CategoryName.Trim(), Amount = FinancialOperationHasher.NormalizeAmount(command.Amount),
        PaidTo = command.PaidTo.Trim(), Description = (string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim()),
        command.ExpenseDate, command.Treatment, command.Till, command.TenderLine, command.SettlementEvidence });

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
