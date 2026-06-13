using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;

internal sealed class VoidCreditNoteCommandValidator : AbstractValidator<VoidCreditNoteCommand>
{
    public VoidCreditNoteCommandValidator()
    {
        RuleFor(x => x.ActorUserId)
            .NotEmpty();

        RuleFor(x => x.ActiveShopId)
            .NotEmpty();

        RuleFor(x => x.Code)
            .NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithErrorCode(Errors.CreditNote.VoidReasonRequired.Code)
            .WithMessage(Errors.CreditNote.VoidReasonRequired.Description);
    }
}
