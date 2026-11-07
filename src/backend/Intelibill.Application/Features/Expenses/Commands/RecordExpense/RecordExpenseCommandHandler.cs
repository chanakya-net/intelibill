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
    IUnitOfWork unitOfWork)
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
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Expense.Forbidden;

        return await PostingTransaction.RunAsync(unitOfWork, ct => PostAsync(command, ct), cancellationToken);
    }

    private async Task<ErrorOr<ExpenseDto>> PostAsync(RecordExpenseCommand command, CancellationToken cancellationToken)
    {
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(command.ShopId, command.ExpenseDate, cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        // Upsert category
        var categoryName = command.CategoryName.Trim();
        var category = await expenseCategoryRepository.GetByNameAsync(command.ShopId, categoryName, cancellationToken);
        if (category is null)
        {
            category = ExpenseCategory.Create(command.ShopId, categoryName, DateTimeOffset.UtcNow);
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var expense = Expense.Create(
            command.ShopId,
            category.Id,
            command.Amount,
            command.PaidTo,
            command.Description,
            command.ExpenseDate,
            command.UserId, command.Treatment);

        await expenseRepository.AddAsync(expense, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(expense, category.Name);
    }

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
            e.CreatedAt, e.Treatment);
}
