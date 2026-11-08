using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Expenses.Commands.RecordExpense;

public sealed record RecordExpenseCommand(
    Guid UserId,
    Guid ShopId,
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate,
    ExpenseTreatment Treatment = ExpenseTreatment.Unclassified,
    string? OperationKey = null, TillPostingContext? Till = null, TenderLine? TenderLine = null, TillSettlementEvidence? SettlementEvidence = null);
