namespace Intelibill.Application.Features.Expenses.Queries.GetExpenses;

public sealed record GetExpensesQuery(
    Guid UserId,
    Guid ShopId,
    string? SearchTerm,
    int PageNumber = 1,
    int PageSize = 20);
