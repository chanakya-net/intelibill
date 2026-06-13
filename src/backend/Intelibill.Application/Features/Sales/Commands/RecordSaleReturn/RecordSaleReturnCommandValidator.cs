using FluentValidation;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

public sealed class RecordSaleReturnCommandValidator : AbstractValidator<RecordSaleReturnCommand>
{
    public RecordSaleReturnCommandValidator()
    {
        RuleFor(x => x.CreditNoteReason)
            .Must(reason => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length <= 1000)
            .WithMessage("Credit note reason must be 1000 characters or less.");
    }
}
