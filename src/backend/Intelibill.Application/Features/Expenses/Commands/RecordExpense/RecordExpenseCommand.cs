namespace Intelibill.Application.Features.Expenses.Commands.RecordExpense;

public sealed record RecordExpenseCommand(
    Guid UserId,
    Guid ShopId,
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate);
