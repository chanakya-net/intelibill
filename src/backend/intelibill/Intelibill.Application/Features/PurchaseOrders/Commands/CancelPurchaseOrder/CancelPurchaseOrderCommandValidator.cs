using FluentValidation;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

internal sealed class CancelPurchaseOrderCommandValidator : AbstractValidator<CancelPurchaseOrderCommand>
{
    public CancelPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}
