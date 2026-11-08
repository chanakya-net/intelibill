using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Expenses.Commands.CorrectExpense;

public sealed record CorrectExpenseCommand(
    Guid UserId,
    Guid ShopId,
    Guid OriginalExpenseId,
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate,
    ExpenseTreatment? Treatment = null,
    string? OperationKey = null, TillPostingContext? Till = null, TillSettlementEvidence? SettlementEvidence = null);
