using FluentValidation;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills.Commands.CreateTill;

public sealed class CreateTillCommandValidator : AbstractValidator<CreateTillCommand>
{
    public CreateTillCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperationKey).NotEmpty().Must(x => x is not null && x.Trim().Length <= 128);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
