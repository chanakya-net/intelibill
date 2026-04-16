using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandValidator : AbstractValidator<RecordSaleCommand>
{
    public RecordSaleCommandValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty()
            .WithErrorCode(Errors.Sale.ItemsRequired.Code)
            .WithMessage(Errors.Sale.ItemsRequired.Description);

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Barcode)
                .NotEmpty()
                .WithErrorCode("Sale.BarcodeRequired")
                .WithMessage("Barcode is required.");

            item.RuleFor(i => i.BatchNumber)
                .NotEmpty()
                .WithErrorCode("Sale.BatchNumberRequired")
                .WithMessage("Batch number is required.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithErrorCode("Sale.QuantityMustBePositive")
                .WithMessage("Quantity must be greater than zero.");

            item.RuleFor(i => i.CostPrice)
                .GreaterThanOrEqualTo(0)
                .WithErrorCode("Sale.CostPriceInvalid")
                .WithMessage("Cost price cannot be negative.");

            item.RuleFor(i => i.SalesPrice)
                .GreaterThanOrEqualTo(0)
                .WithErrorCode("Sale.SalesPriceInvalid")
                .WithMessage("Sales price cannot be negative.");

            item.RuleFor(i => i.Mrp)
                .GreaterThanOrEqualTo(0)
                .WithErrorCode("Sale.MrpInvalid")
                .WithMessage("MRP cannot be negative.");

            item.RuleFor(i => i.TaxRatePercent)
                .InclusiveBetween(0, 100)
                .WithErrorCode("Sale.TaxRateOutOfRange")
                .WithMessage("Tax rate must be between 0 and 100.");
        });
    }
}
