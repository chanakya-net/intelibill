using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Expenses.Commands.CorrectExpense;

internal sealed class CorrectExpenseCommandValidator : AbstractValidator<CorrectExpenseCommand>
{
    public CorrectExpenseCommandValidator()
    {
        RuleFor(x => x.CategoryName)
            .NotEmpty().WithErrorCode(Errors.Expense.CategoryNameRequired.Code)
            .MaximumLength(100);

        RuleFor(x => x.PaidTo)
            .NotEmpty().WithErrorCode(Errors.Expense.PaidToRequired.Code)
            .MaximumLength(255);

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithErrorCode(Errors.Expense.AmountMustBePositive.Code);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}
