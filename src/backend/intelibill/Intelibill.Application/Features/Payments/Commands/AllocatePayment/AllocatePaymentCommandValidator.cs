using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Payments.Commands.AllocatePayment;

internal sealed class AllocatePaymentCommandValidator : AbstractValidator<AllocatePaymentCommand>
{
    public AllocatePaymentCommandValidator()
    {
        RuleFor(x => x.Allocations)
            .NotEmpty()
            .WithErrorCode(Errors.Payment.AllocationsRequired.Code)
            .WithMessage(Errors.Payment.AllocationsRequired.Description);

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
