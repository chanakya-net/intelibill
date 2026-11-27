using Intelibill.Domain.Entities;
using FluentValidation;
using Intelibill.Domain.Enums;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Expenses.Commands.RecordExpense;

internal sealed class RecordExpenseCommandValidator : AbstractValidator<RecordExpenseCommand>
{
    public RecordExpenseCommandValidator()
    {
        RuleFor(x => x.OperationKey)
            .Must(key => key is null || key.Trim().Length is >= 1 and <= OperationReceipt.MaxKeyLength)
            .WithErrorCode(Errors.Operation.InvalidKey.Code);

        RuleFor(x => x.Treatment).Must(t => (Enum.IsDefined(t) && t != ExpenseTreatment.SupplierSettlement))
            .WithErrorCode("Expense.InvalidTreatment");

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
