using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;

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

            item.RuleFor(i => i.InventoryBatchId)
                .NotEmpty()
                .WithErrorCode("Sale.InventoryBatchIdRequired")
                .WithMessage("Inventory batch id is required.");

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

        RuleFor(x => x.PaidAmount)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(Errors.Sale.PaidAmountInvalid.Code)
            .WithMessage(Errors.Sale.PaidAmountInvalid.Description);

        RuleFor(x => x.DueAmount)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(Errors.Sale.DueAmountInvalid.Code)
            .WithMessage(Errors.Sale.DueAmountInvalid.Description);

        RuleFor(x => x)
            .Must(x => x.PaymentMethod != PaymentMethod.Credit || x.DueAmount > 0)
            .WithErrorCode(Errors.Sale.CreditRequiresDueAmount.Code)
            .WithMessage(Errors.Sale.CreditRequiresDueAmount.Description);

        RuleFor(x => x)
            .Must(x => x.DueAmount <= 0 || x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.CustomerPhone))
            .WithErrorCode(Errors.Sale.CustomerIdentityRequiredForDue.Code)
            .WithMessage(Errors.Sale.CustomerIdentityRequiredForDue.Description);
    }
}
