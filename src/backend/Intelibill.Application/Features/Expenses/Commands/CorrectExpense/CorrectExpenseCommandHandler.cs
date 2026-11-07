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

        if (command.Treatment.HasValue && (!Enum.IsDefined(command.Treatment.Value)
            || (command.Treatment == ExpenseTreatment.SupplierSettlement && original.Treatment != ExpenseTreatment.SupplierSettlement)
            || (original.Treatment == ExpenseTreatment.SupplierSettlement && command.Treatment != original.Treatment)))
            return Error.Validation("Expense.InvalidTreatment", "Supplier settlements retain their source treatment; other expenses require a valid treatment.");

        if (original.IsVoided)
            return Errors.Expense.AlreadyVoided;

        return await PostingTransaction.RunAsync(unitOfWork, ct => CorrectAsync(command, original, ct), cancellationToken);
    }

    private async Task<ErrorOr<ExpenseDto>> CorrectAsync(
        CorrectExpenseCommand command,
        Expense original,
        CancellationToken cancellationToken)
    {
        // The original is voided in its own month and the replacement lands in the new one: both
        // must be open, taken lowest month first.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ShopId, [original.ExpenseDate, command.ExpenseDate], cancellationToken);
        if (fence.IsError)
            return fence.Errors;

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
            command.OriginalExpenseId, command.Treatment ?? original.Treatment, original.SupplierLedgerEntryId);

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
            e.CreatedAt, e.Treatment);
}
