using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Barcodes;

namespace Intelibill.Application.Features.Items.Barcodes.PrintBarcodeLabels;

internal sealed class PrintBarcodeLabelsCommandValidator : AbstractValidator<PrintBarcodeLabelsCommand>
{
    internal const int MaxTotalQuantity = 500;

    public PrintBarcodeLabelsCommandValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithErrorCode(Errors.Item.BarcodeLabelItemsRequired.Code);

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.ItemId)
                    .NotEmpty()
                    .WithErrorCode(Errors.Item.BarcodeLabelItemIdRequired.Code);

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithErrorCode(Errors.Item.BarcodeLabelQuantityInvalid.Code);
            });

        RuleFor(x => x.Items)
            .Must(items => items.Sum(i => i.Quantity) <= MaxTotalQuantity)
            .WithErrorCode(Errors.Item.BarcodeLabelQuantityLimitExceeded(MaxTotalQuantity).Code);
    }
}
