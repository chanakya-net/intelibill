using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.AcceptShopInvitation;

internal sealed class AcceptShopInvitationCommandValidator : AbstractValidator<AcceptShopInvitationCommand>
{
    public AcceptShopInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}
