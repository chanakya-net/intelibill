using FluentValidation;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

internal sealed class ReceivePurchaseOrderCommandValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderCommandValidator()
    {
        RuleFor(c => c.PurchaseOrderId).NotEmpty();
        RuleFor(c => c.Lines).Must(lines => lines.Count == 1)
            .WithErrorCode("PurchaseOrder.ReceiptLineRequired")
            .WithMessage("Exactly one receipt line is required.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PurchaseOrderLineId).NotEmpty();
            line.RuleFor(l => l.BatchNumber).NotEmpty().MaximumLength(80);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.TotalPurchaseCost).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Mrp).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.SalesPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(l => l.Mrp);
            line.RuleFor(l => l.TaxRatePercent).InclusiveBetween(0, 100);
        });
    }
}
