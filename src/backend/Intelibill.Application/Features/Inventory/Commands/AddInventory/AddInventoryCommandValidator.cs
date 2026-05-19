using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

internal sealed class AddInventoryCommandValidator : AbstractValidator<AddInventoryCommand>
{
    public AddInventoryCommandValidator()
    {
        RuleFor(x => x.ItemName)
            .NotEmpty().WithErrorCode(Errors.Inventory.ItemNameRequired.Code)
            .MaximumLength(180);

        RuleFor(x => x.Barcode)
            .NotEmpty().WithErrorCode(Errors.Inventory.BarcodeRequired.Code)
            .MaximumLength(128);

        RuleFor(x => x.Uom)
            .NotEmpty().WithErrorCode(Errors.Inventory.UomRequired.Code)
            .MaximumLength(32);

        RuleFor(x => x.BatchNumber)
            .NotEmpty().WithErrorCode(Errors.Inventory.BatchNumberRequired.Code)
            .MaximumLength(80);

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithErrorCode(Errors.Inventory.QuantityMustBePositive.Code);

        RuleFor(x => x.TotalPurchaseCost)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.Mrp)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.SalesPrice)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.SalesPrice)
            .LessThanOrEqualTo(x => x.Mrp);

        RuleFor(x => x.TaxRatePercent)
            .InclusiveBetween(0, 100);
    }
}
