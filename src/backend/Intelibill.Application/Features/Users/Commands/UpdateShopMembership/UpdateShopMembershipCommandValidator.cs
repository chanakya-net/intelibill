using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.UpdateShopMembership;

internal sealed class UpdateShopMembershipCommandValidator : AbstractValidator<UpdateShopMembershipCommand>
{
    public UpdateShopMembershipCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Status).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}
