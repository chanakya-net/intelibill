namespace Intelibill.Application.Features.Expenses.Commands.CorrectExpense;

public sealed record CorrectExpenseCommand(
    Guid UserId,
    Guid ShopId,
    Guid OriginalExpenseId,
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate);
