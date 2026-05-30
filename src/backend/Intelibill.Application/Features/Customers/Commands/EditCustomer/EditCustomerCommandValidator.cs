using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

public sealed class EditCustomerCommandValidator : AbstractValidator<EditCustomerCommand>
{
    public EditCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty();

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

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0m)
            .WithErrorCode(Errors.Customer.CreditLimitInvalid.Code)
            .WithMessage(Errors.Customer.CreditLimitInvalid.Description)
            .LessThanOrEqualTo(99999999.99m)
            .WithErrorCode(Errors.Customer.CreditLimitInvalid.Code)
            .WithMessage(Errors.Customer.CreditLimitInvalid.Description);
    }
}
