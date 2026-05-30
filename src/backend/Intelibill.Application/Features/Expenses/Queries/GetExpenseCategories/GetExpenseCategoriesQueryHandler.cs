using ErrorOr;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Expenses.Queries.GetExpenseCategories;

public sealed class GetExpenseCategoriesQueryHandler(IExpenseCategoryRepository expenseCategoryRepository)
{
    public async Task<ErrorOr<IReadOnlyList<ExpenseCategoryDto>>> Handle(
        GetExpenseCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        var categories = await expenseCategoryRepository.GetByShopAsync(query.ShopId, cancellationToken);

        return categories
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name))
            .ToList();
    }
}
