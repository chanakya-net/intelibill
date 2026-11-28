using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>Resolves the "Supplier Payments" expense category, builds a supplier payment's linked expense and stages both.</summary>
public sealed class SupplierPaymentExpensePoster(
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IBusinessClock businessClock)
{
    internal async Task<(ExpenseCategory Category, bool IsNew)> ResolveExpenseCategoryAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        const string categoryName = "Supplier Payments";
        var category = await expenseCategoryRepository.GetByNameAsync(shopId, categoryName, cancellationToken);
        var newCategory = category is null;
        category ??= ExpenseCategory.Create(shopId, categoryName, businessClock.UtcNow);
        return (category, newCategory);
    }

    internal static Expense CreateExpense(
        Guid shopId,
        Guid categoryId,
        decimal amount,
        string? notes,
        DateOnly paymentDate,
        Guid actorUserId,
        Guid ledgerEntryId) =>
        Expense.CreateFromSupplierPayment(
            shopId,
            categoryId,
            amount,
            "Supplier",
            notes,
            paymentDate,
            actorUserId,
            ledgerEntryId);

    internal async Task StageCategoryAsync(ExpenseCategory category, bool newCategory, CancellationToken cancellationToken)
    {
        if (newCategory)
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
    }

    internal async Task StageExpenseAsync(Expense expense, CancellationToken cancellationToken) =>
        await expenseRepository.AddAsync(expense, cancellationToken);
}
