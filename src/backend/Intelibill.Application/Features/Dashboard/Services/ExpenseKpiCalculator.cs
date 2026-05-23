using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Dashboard.Services;

internal static class ExpenseKpiCalculator
{
    internal sealed record ExpenseKpis(
        decimal ExpenseRecorded,
        decimal ExpenseCorrection,
        decimal NetExpense);

    internal static ExpenseKpis CalculateExpenseKpis(IReadOnlyCollection<Expense> expenses)
    {
        var recorded = expenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
        var correction = expenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);
        return new ExpenseKpis(recorded, correction, recorded + correction);
    }
}
