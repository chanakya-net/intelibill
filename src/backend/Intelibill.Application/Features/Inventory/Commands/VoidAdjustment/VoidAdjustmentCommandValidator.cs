using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;

internal sealed class VoidAdjustmentCommandValidator : AbstractValidator<VoidAdjustmentCommand>
{
    public VoidAdjustmentCommandValidator()
    {
        RuleFor(x => x.AdjustmentId)
            .NotEmpty();

        RuleFor(x => x.ActorUserId)
            .NotEmpty();

        RuleFor(x => x.ActiveShopId)
            .NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithErrorCode(Errors.Inventory.AdjustmentVoidReasonRequired.Code)
            .WithMessage(Errors.Inventory.AdjustmentVoidReasonRequired.Description);
    }
}
