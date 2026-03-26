using FluentValidation;

namespace InventoryAI.Application.Features.Auth.Commands.LoginWithEmail;

internal sealed class LoginWithEmailCommandValidator : AbstractValidator<LoginWithEmailCommand>
{
    public LoginWithEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
