using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

internal sealed class UpdatePurchaseOrderDraftCommandValidator : AbstractValidator<UpdatePurchaseOrderDraftCommand>
{
    public UpdatePurchaseOrderDraftCommandValidator()
    {
        RuleFor(x => x.Notes)
            .MaximumLength(1000);

        RuleFor(x => x.SupplierReferenceNumber)
            .MaximumLength(100);

        RuleFor(x => x.Lines)
            .NotNull();

        RuleForEach(x => x.Lines)
            .ChildRules(line =>
            {
                line.RuleFor(l => l.ItemId)
                    .NotEmpty()
                    .WithErrorCode(Errors.PurchaseOrder.LineDescriptionRequired.Code);

                line.RuleFor(l => l.Description)
                    .NotEmpty()
                    .WithErrorCode(Errors.PurchaseOrder.LineDescriptionRequired.Code)
                    .MaximumLength(500);

                line.RuleFor(l => l.ExpectedQuantity)
                    .GreaterThan(0)
                    .WithErrorCode(Errors.PurchaseOrder.InvalidLineQuantity.Code);

                line.RuleFor(l => l.UnitCost)
                    .GreaterThanOrEqualTo(0)
                    .WithErrorCode(Errors.PurchaseOrder.InvalidLineUnitCost.Code);
            });
    }
}
