using FluentValidation;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills.Commands.OpenTill;

public sealed class OpenTillCommandValidator : AbstractValidator<OpenTillCommand>
{
    public OpenTillCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperationKey).NotEmpty().Must(x => x is not null && x.Trim().Length <= 128);
        RuleFor(x => x.TillId).NotEmpty();
        RuleFor(x => x.CashierId).NotEmpty();
        RuleFor(x => x.OpeningFloat).Must(x => TillMoney.IsValid(x) && x >= 0);
        RuleFor(x => x.OverrideReason).MaximumLength(500);
    }
}
