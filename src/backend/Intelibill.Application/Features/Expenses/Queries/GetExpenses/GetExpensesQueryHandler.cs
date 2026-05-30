using ErrorOr;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Expenses.Queries.GetExpenses;

public sealed class GetExpensesQueryHandler(IExpenseRepository expenseRepository)
{
    public async Task<ErrorOr<PaginatedList<ExpenseListItemDto>>> Handle(
        GetExpensesQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await expenseRepository.GetPagedByShopAsync(
            query.ShopId,
            query.SearchTerm,
            query.PageNumber,
            query.PageSize,
            cancellationToken);

        var dtos = items.Select(e => new ExpenseListItemDto(
            e.Id,
            e.Amount,
            e.Category.Name,
            e.PaidTo,
            e.ExpenseDate,
            e.IsVoided)).ToList();

        return new PaginatedList<ExpenseListItemDto>(dtos, totalCount, query.PageNumber, query.PageSize);
    }
}
