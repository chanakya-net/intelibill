using FluentValidation;

namespace Intelibill.Application.Features.Auth.Commands.Login;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Identifier)
            .Must(identifier => !string.IsNullOrWhiteSpace(identifier));

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
