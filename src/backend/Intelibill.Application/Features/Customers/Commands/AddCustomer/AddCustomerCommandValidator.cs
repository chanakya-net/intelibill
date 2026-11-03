using FluentValidation;
using Gstin = Intelibill.Domain.Common.Gstin;
using GstStateCodes = Intelibill.Domain.Common.GstStateCodes;
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

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0m)
            .WithErrorCode(Errors.Customer.CreditLimitInvalid.Code)
            .WithMessage(Errors.Customer.CreditLimitInvalid.Description)
            .PrecisionScale(10, 2, ignoreTrailingZeros: true)
            .WithErrorCode(Errors.Customer.CreditLimitInvalid.Code)
            .WithMessage(Errors.Customer.CreditLimitInvalid.Description)
            .LessThanOrEqualTo(99999999.99m)
            .WithErrorCode(Errors.Customer.CreditLimitInvalid.Code)
            .WithMessage(Errors.Customer.CreditLimitInvalid.Description);

        RuleFor(x => x.Gstin)
            .Must(Gstin.IsValid)
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin))
            .WithErrorCode(Errors.Customer.GstinInvalid.Code)
            .WithMessage(Errors.Customer.GstinInvalid.Description);

        RuleFor(x => x.StateCode)
            .Must(GstStateCodes.IsKnown)
            .When(x => !string.IsNullOrWhiteSpace(x.StateCode))
            .WithErrorCode(Errors.Customer.StateCodeInvalid.Code)
            .WithMessage(Errors.Customer.StateCodeInvalid.Description);

        RuleFor(x => x)
            .Must(x => string.Equals(Gstin.StateCodeOf(x.Gstin!), x.StateCode!.Trim(), StringComparison.Ordinal))
            .When(x => Gstin.IsValid(x.Gstin) && !string.IsNullOrWhiteSpace(x.StateCode))
            .WithName(nameof(AddCustomerCommand.StateCode))
            .WithErrorCode(Errors.Customer.GstinStateMismatch.Code)
            .WithMessage(Errors.Customer.GstinStateMismatch.Description);
    }
}
