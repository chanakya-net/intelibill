using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Payments.Commands.ReversePayment;

internal sealed class ReversePaymentCommandValidator : AbstractValidator<ReversePaymentCommand>
{
    public ReversePaymentCommandValidator()
    {
        RuleFor(x => x.Reason)
            .Must(reason => reason is not null && reason.Trim().Length is >= PaymentReversal.MinReasonLength and <= PaymentReversal.MaxReasonLength)
            .WithErrorCode(Errors.Payment.ReversalReasonInvalid.Code)
            .WithMessage(Errors.Payment.ReversalReasonInvalid.Description);

        RuleFor(x => x.OperationKey)
            .Must(key => !string.IsNullOrWhiteSpace(key))
            .WithErrorCode(Errors.Payment.OperationKeyRequired.Code)
            .WithMessage(Errors.Payment.OperationKeyRequired.Description)
            .Must(key => key is null || key.Trim().Length <= OperationReceipt.MaxKeyLength)
            .WithErrorCode(Errors.Operation.InvalidKey.Code)
            .WithMessage(Errors.Operation.InvalidKey.Description);
    }
}
