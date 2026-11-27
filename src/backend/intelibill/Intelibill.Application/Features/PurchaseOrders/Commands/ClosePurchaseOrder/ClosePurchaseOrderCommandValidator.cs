using FluentValidation;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ClosePurchaseOrder;

internal sealed class ClosePurchaseOrderCommandValidator : AbstractValidator<ClosePurchaseOrderCommand>
{
    public ClosePurchaseOrderCommandValidator()
    {
        RuleFor(c => c.PurchaseOrderId).NotEmpty();
        RuleFor(c => c.Reason)
            .NotEmpty()
            .WithErrorCode("PurchaseOrder.CloseReasonRequired")
            .WithMessage("Close reason is required.")
            .MaximumLength(500);
    }
}
