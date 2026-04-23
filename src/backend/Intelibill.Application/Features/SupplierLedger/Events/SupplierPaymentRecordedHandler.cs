using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierLedger.Events;

public sealed class SupplierPaymentRecordedHandler(
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(
        SupplierPaymentRecorded evt,
        CancellationToken cancellationToken)
    {
        const string categoryName = "Supplier Payments";

        var category = await expenseCategoryRepository.GetByNameAsync(evt.ShopId, categoryName, cancellationToken);
        if (category is null)
        {
            category = ExpenseCategory.Create(evt.ShopId, categoryName, DateTimeOffset.UtcNow);
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var expense = Expense.CreateFromSupplierPayment(
            evt.ShopId,
            category.Id,
            evt.Amount,
            "Supplier",
            evt.Notes,
            evt.EntryDate,
            evt.CreatedBy,
            evt.SupplierLedgerEntryId);

        await expenseRepository.AddAsync(expense, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
