using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

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

        RuleFor(x => x.Amount)
            .Must(amount => decimal.Round(amount, 2) == amount)
            .WithErrorCode("Payment.AmountInvalid")
            .WithMessage("Payment amount must have at most two decimals.");

        RuleFor(x => x.Method)
            .Must(method => method is null || Enum.IsDefined(method.Value))
            .WithErrorCode(Errors.Payment.MethodInvalid.Code)
            .WithMessage(Errors.Payment.MethodInvalid.Description);

        RuleFor(x => x.ExternalReference)
            .MaximumLength(PaymentReceipt.MaxExternalReferenceLength)
            .WithErrorCode(Errors.Payment.ReferenceTooLong.Code)
            .WithMessage(Errors.Payment.ReferenceTooLong.Description);

        RuleForEach(x => x.Allocations)
            .Must(a => a.Amount > 0m && decimal.Round(a.Amount, 2) == a.Amount)
            .WithErrorCode(Errors.Payment.AllocationAmountInvalid.Code)
            .WithMessage(Errors.Payment.AllocationAmountInvalid.Description);

        RuleFor(x => x.OperationKey)
            .Must(key => key is null || key.Trim().Length is >= 1 and <= OperationReceipt.MaxKeyLength)
            .WithErrorCode(Errors.Operation.InvalidKey.Code)
            .WithMessage(Errors.Operation.InvalidKey.Description);
    }
}
