using FluentValidation;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Inventory.Commands.UpdateBatchPrices;

internal sealed class UpdateBatchPricesCommandValidator : AbstractValidator<UpdateBatchPricesCommand>
{
    public UpdateBatchPricesCommandValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ActiveShopId).NotEmpty();
        RuleFor(x => x.BatchNumber).MaximumLength(80);
        RuleFor(x => x.Mrp).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.SalesPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true).LessThanOrEqualTo(x => x.Mrp);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 100).PrecisionScale(5, 2, true);
        RuleFor(x => x.Reason).MaximumLength(BatchPriceRevision.MaxReasonLength);
    }
}
