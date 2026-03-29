using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.AddShopUser;

internal sealed class AddShopUserCommandValidator : AbstractValidator<AddShopUserCommand>
{
    public AddShopUserCommandValidator()
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

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100);

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .Equal(x => x.Password)
            .WithMessage("Password and confirmation password must match.");

        RuleFor(x => x.Role)
            .NotEmpty()
            .MaximumLength(32);
    }
}
