using FluentValidation;

namespace InventoryAI.Application.Features.Auth.Commands.RegisterWithPhone;

internal sealed class RegisterWithPhoneCommandValidator : AbstractValidator<RegisterWithPhoneCommand>
{
    public RegisterWithPhoneCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("Phone number must be in E.164 format (e.g. +14155552671).");

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
