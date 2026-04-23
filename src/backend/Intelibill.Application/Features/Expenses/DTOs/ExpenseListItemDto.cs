namespace Intelibill.Application.Features.Expenses.DTOs;

public sealed record ExpenseListItemDto(
    Guid Id,
    decimal Amount,
    string CategoryName,
    string PaidTo,
    DateOnly ExpenseDate,
    bool IsVoided);
