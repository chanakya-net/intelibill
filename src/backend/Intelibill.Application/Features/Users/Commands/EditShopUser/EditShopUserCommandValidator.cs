using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.EditShopUser;

internal sealed class EditShopUserCommandValidator : AbstractValidator<EditShopUserCommand>
{
    public EditShopUserCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .MaximumLength(32)
            .Matches("^\\+?[0-9]{7,15}$");

        RuleFor(x => x.Role)
            .NotEmpty()
            .MaximumLength(32);
    }
}
