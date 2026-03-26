using FluentValidation;

namespace InventoryAI.Application.Features.Auth.Commands.RevokeToken;

internal sealed class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    public RevokeTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
