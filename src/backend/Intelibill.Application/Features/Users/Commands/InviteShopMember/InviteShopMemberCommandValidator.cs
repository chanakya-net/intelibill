using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.InviteShopMember;

internal sealed class InviteShopMemberCommandValidator : AbstractValidator<InviteShopMemberCommand>
{
    public InviteShopMemberCommandValidator()
    {
        RuleFor(x => x.ShopId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(32);
    }
}
