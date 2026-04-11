using FluentValidation;

namespace Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;

public sealed class UpdateInventoryBatchCommandValidator : AbstractValidator<UpdateInventoryBatchCommand>
{
    public UpdateInventoryBatchCommandValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.NewBatchNumber).MaximumLength(80);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Mrp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SalesPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(x => x.Mrp);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100);
    }
}
