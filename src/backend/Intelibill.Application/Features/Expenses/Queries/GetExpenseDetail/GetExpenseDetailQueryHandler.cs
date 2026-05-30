using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Expenses.Queries.GetExpenseDetail;

public sealed class GetExpenseDetailQueryHandler(IExpenseRepository expenseRepository)
{
    public async Task<ErrorOr<ExpenseDto>> Handle(
        GetExpenseDetailQuery query,
        CancellationToken cancellationToken)
    {
        var expense = await expenseRepository.GetByIdAsync(query.ExpenseId, cancellationToken);

        if (expense is null || expense.ShopId != query.ShopId)
        {
            return Errors.Expense.NotFound;
        }

        return ToDto(expense);
    }

    private static ExpenseDto ToDto(Domain.Entities.Expense e) =>
        new(
            e.Id,
            e.ShopId,
            e.CategoryId,
            e.Category.Name,
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
