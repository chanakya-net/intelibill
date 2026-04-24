using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;

public sealed class RecordCustomerPaymentCommandValidator : AbstractValidator<RecordCustomerPaymentCommand>
{
    public RecordCustomerPaymentCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithErrorCode(Errors.Customer.PaymentAmountMustBePositive.Code)
            .WithMessage(Errors.Customer.PaymentAmountMustBePositive.Description);

        RuleFor(x => x.PaymentDate)
            .NotEqual(default(DateOnly))
            .WithErrorCode("Customer.PaymentDateRequired")
            .WithMessage("Payment date is required.");

        RuleFor(x => x.Notes)
            .MaximumLength(255)
            .WithErrorCode("Customer.PaymentNotesTooLong")
            .WithMessage("Payment notes must not exceed 255 characters.");
    }
}
