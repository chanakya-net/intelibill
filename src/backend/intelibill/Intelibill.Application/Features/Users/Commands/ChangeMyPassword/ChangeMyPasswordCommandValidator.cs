using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.ChangeMyPassword;

internal sealed class ChangeMyPasswordCommandValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    public ChangeMyPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100)
            .NotEqual(x => x.CurrentPassword);
    }
}
