using FluentValidation;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Expenses.Queries.GetExpenses;

public sealed class GetExpensesQueryValidator : AbstractValidator<GetExpensesQuery>
{
    public GetExpensesQueryValidator()
    {
        RuleFor(q => q.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) ||
                status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ||
                status.Trim().Equals("active", StringComparison.OrdinalIgnoreCase) ||
                status.Trim().Equals("voided", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode("Expense.InvalidStatus")
            .WithMessage("Status must be all, active, or voided.");
        RuleFor(q => q.PageNumber)
            .Must((query, page) => page >= 1 && ((long)page - 1) * query.PageSize <= int.MaxValue)
            .WithErrorCode("Expense.InvalidPageNumber")
            .WithMessage("Page must be positive and its offset must not exceed the supported range.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100)
            .WithErrorCode("Expense.InvalidPageSize")
            .WithMessage("Page size must be between 1 and 100.");
    }

    public static ExpenseStatusFilter ParseStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "active" => ExpenseStatusFilter.Active,
        "voided" => ExpenseStatusFilter.Voided,
        _ => ExpenseStatusFilter.All,
    };
}
