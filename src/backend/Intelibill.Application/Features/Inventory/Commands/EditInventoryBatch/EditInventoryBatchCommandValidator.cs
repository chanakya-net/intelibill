using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Inventory.Commands.EditInventoryBatch;

internal sealed class EditInventoryBatchCommandValidator : AbstractValidator<EditInventoryBatchCommand>
{
    public EditInventoryBatchCommandValidator()
    {
        RuleFor(x => x.InventoryBatchId)
            .NotEqual(Guid.Empty);

        RuleFor(x => x.BatchNumber)
            .NotEmpty().WithErrorCode(Errors.Inventory.BatchNumberRequired.Code)
            .MaximumLength(80);

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.CostPrice)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.Mrp)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.SalesPrice)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Mrp);

        RuleFor(x => x.TaxRatePercent)
            .InclusiveBetween(0, 100);

        RuleFor(x => x.Notes)
            .MaximumLength(255)
            .When(x => x.Notes is not null);
    }
}
