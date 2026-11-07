using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

internal sealed class ReceivePurchaseOrderCommandValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderCommandValidator()
    {
        RuleFor(c => c.PurchaseOrderId).NotEmpty();
        RuleFor(c => c.Lines).NotEmpty()
            .WithErrorCode("PurchaseOrder.ReceiptLineRequired")
            .WithMessage("At least one receipt line is required.");

        RuleFor(c => c.OperationId).NotEqual(Guid.Empty).When(c => c.OperationId.HasValue);

        RuleFor(c => c.Lines).Custom((lines, context) =>
        {
            var duplicateIds = lines.Where(l => l.ClientLineId.HasValue).GroupBy(l => l.ClientLineId)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            for (var i = 0; i < lines.Count; i++)
                if (duplicateIds.Contains(lines[i].ClientLineId))
                    context.AddFailure(new FluentValidation.Results.ValidationFailure($"Lines[{i}].ClientLineId", Errors.PurchaseOrder.DuplicateClientLineId.Description)
                    { ErrorCode = Errors.PurchaseOrder.DuplicateClientLineId.Code });
        });

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PurchaseOrderLineId).NotEmpty();
            line.RuleFor(l => l.ClientLineId).NotEqual(Guid.Empty)
                .WithErrorCode(Errors.PurchaseOrder.ClientLineIdInvalid.Code)
                .WithMessage(Errors.PurchaseOrder.ClientLineIdInvalid.Description)
                .When(l => l.ClientLineId.HasValue);

            line.RuleFor(l => l.Barcode).NotEmpty().MaximumLength(120);
            line.RuleFor(l => l.BatchNumber).NotEmpty().MaximumLength(80);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.TotalPurchaseCost).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Mrp).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.SalesPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(l => l.Mrp);
            line.RuleFor(l => l.TaxRatePercent).InclusiveBetween(0, 100);
        });
    }
}
