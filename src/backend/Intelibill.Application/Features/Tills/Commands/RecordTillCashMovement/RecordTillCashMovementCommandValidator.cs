using FluentValidation;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;

public sealed class RecordTillCashMovementCommandValidator : AbstractValidator<RecordTillCashMovementCommand>
{
    public RecordTillCashMovementCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperationKey).NotEmpty().Must(x => x is not null && x.Trim().Length <= 128);
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Direction).Must(x => x is 1m or -1m);
        RuleFor(x => x.Amount).Must(x => TillMoney.IsValid(x) && x > 0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ExpectedVersion).NotEqual(0u);
        RuleFor(x => x.OverrideReason).MaximumLength(500);
    }
}
