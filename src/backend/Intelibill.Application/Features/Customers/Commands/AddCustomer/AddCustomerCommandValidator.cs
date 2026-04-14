using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Customers.Commands.AddCustomer;

public sealed class AddCustomerCommandValidator : AbstractValidator<AddCustomerCommand>
{
    public AddCustomerCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithErrorCode(Errors.Customer.NameRequired.Code)
            .WithMessage(Errors.Customer.NameRequired.Description)
            .MaximumLength(180);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithErrorCode(Errors.Customer.PhoneNumberRequired.Code)
            .WithMessage(Errors.Customer.PhoneNumberRequired.Description)
            .MaximumLength(32)
            .Matches(@"^\+?[0-9]{7,15}$")
            .WithErrorCode(Errors.Customer.PhoneNumberInvalid.Code)
            .WithMessage(Errors.Customer.PhoneNumberInvalid.Description);

        RuleFor(x => x.Address)
            .MaximumLength(320)
            .When(x => x.Address is not null);
    }
}
