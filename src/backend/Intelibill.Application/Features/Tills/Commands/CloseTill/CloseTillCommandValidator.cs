using FluentValidation;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills.Commands.CloseTill;

public sealed class CloseTillCommandValidator : AbstractValidator<CloseTillCommand>
{
    public CloseTillCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperationKey).NotEmpty().Must(x => x is not null && x.Trim().Length <= 128);
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.CountedCash).Must(x => TillMoney.IsValid(x) && x >= 0);
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.ExpectedVersion).NotEqual(0u);
        RuleFor(x => x.OverrideReason).MaximumLength(500);
    }
}
