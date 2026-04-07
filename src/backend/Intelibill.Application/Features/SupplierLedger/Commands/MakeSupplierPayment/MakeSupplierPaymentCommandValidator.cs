using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

internal sealed class MakeSupplierPaymentCommandValidator : AbstractValidator<MakeSupplierPaymentCommand>
{
    public MakeSupplierPaymentCommandValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithErrorCode(Errors.Supplier.PaymentAmountMustBePositive.Code);

        RuleFor(x => x.PaymentDate)
            .NotEqual(DateOnly.MinValue);

        RuleFor(x => x.Notes)
            .MaximumLength(500)
            .When(x => x.Notes != null);
    }
}
