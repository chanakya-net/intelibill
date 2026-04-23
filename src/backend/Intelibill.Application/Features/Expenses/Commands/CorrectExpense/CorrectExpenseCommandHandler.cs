using ErrorOr;
using Intelibill.Application.Common.Errors;
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
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<ExpenseDto>> HandleAsync(
        CorrectExpenseCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Expense.Forbidden;

        var original = await expenseRepository.GetByIdAsync(command.OriginalExpenseId, cancellationToken);
        if (original is null || original.ShopId != command.ShopId)
            return Errors.Expense.NotFound;

        if (original.IsVoided)
            return Errors.Expense.AlreadyVoided;

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
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
            command.OriginalExpenseId);

        await expenseRepository.AddAsync(corrected, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(corrected, category.Name);
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
            e.CreatedAt);
}
