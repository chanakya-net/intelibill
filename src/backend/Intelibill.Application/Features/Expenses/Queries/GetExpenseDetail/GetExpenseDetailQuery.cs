namespace Intelibill.Application.Features.Expenses.Queries.GetExpenseDetail;

public sealed record GetExpenseDetailQuery(Guid UserId, Guid ShopId, Guid ExpenseId);
