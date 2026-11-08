using FluentValidation;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills.Commands.ReviewTillClose;

public sealed class ReviewTillCloseCommandValidator : AbstractValidator<ReviewTillCloseCommand>
{
    public ReviewTillCloseCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperationKey).NotEmpty().Must(x => x is not null && x.Trim().Length <= 128);
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ExpectedVersion).NotEqual(0u);
    }
}
