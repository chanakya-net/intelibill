using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed class SyncOfflineSalesCommandValidator : AbstractValidator<SyncOfflineSalesCommand>
{
    private const int MaxBatchSize = 50;

    public SyncOfflineSalesCommandValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty()
            .WithErrorCode(Errors.InvoiceLease.DeviceIdRequired.Code)
            .WithMessage(Errors.InvoiceLease.DeviceIdRequired.Description);

        RuleFor(x => x.Sales)
            .NotEmpty()
            .WithErrorCode(Errors.Sale.OfflineSalesRequired.Code)
            .WithMessage(Errors.Sale.OfflineSalesRequired.Description);

        RuleFor(x => x.Sales)
            .Must(sales => sales.Count <= MaxBatchSize)
            .WithErrorCode(Errors.Sale.OfflineBatchLimitExceeded.Code)
            .WithMessage(Errors.Sale.OfflineBatchLimitExceeded.Description);

        RuleForEach(x => x.Sales).ChildRules(sale =>
        {
            sale.RuleFor(s => s.ClientSaleId)
                .NotEmpty()
                .WithErrorCode(Errors.Sale.ClientSaleIdRequired.Code)
                .WithMessage(Errors.Sale.ClientSaleIdRequired.Description);

            sale.RuleFor(s => s.ClientSaleId)
                .MaximumLength(120);

            sale.RuleFor(s => s.InvoiceNumber)
                .NotEmpty()
                .WithErrorCode(Errors.Sale.InvoiceNumberRequired.Code)
                .WithMessage(Errors.Sale.InvoiceNumberRequired.Description);

            sale.RuleFor(s => s.InvoiceNumber)
                .MaximumLength(40);

            sale.RuleFor(s => s.Items)
                .NotEmpty()
                .WithErrorCode(Errors.Sale.ItemsRequired.Code)
                .WithMessage(Errors.Sale.ItemsRequired.Description);

            sale.RuleFor(s => s.PaidAmount)
                .GreaterThanOrEqualTo(0)
                .WithErrorCode(Errors.Sale.PaidAmountInvalid.Code)
                .WithMessage(Errors.Sale.PaidAmountInvalid.Description);

            sale.RuleFor(s => s.DueAmount)
                .GreaterThanOrEqualTo(0)
                .WithErrorCode(Errors.Sale.DueAmountInvalid.Code)
                .WithMessage(Errors.Sale.DueAmountInvalid.Description);

            sale.RuleFor(s => s.TotalAmount)
                .GreaterThanOrEqualTo(0);

            sale.RuleFor(s => s.TotalTaxAmount)
                .GreaterThanOrEqualTo(0);

            sale.RuleFor(s => s.SubtotalBeforeDiscount)
                .GreaterThanOrEqualTo(0);

            sale.RuleFor(s => s.TotalBeforeDiscount)
                .GreaterThanOrEqualTo(0);

            sale.RuleFor(s => s.TotalDiscountAmount)
                .GreaterThanOrEqualTo(0);

            sale.RuleFor(s => s)
                .Must(s => s.PaymentMethod != PaymentMethod.Credit || s.DueAmount > 0)
                .WithErrorCode(Errors.Sale.CreditRequiresDueAmount.Code)
                .WithMessage(Errors.Sale.CreditRequiresDueAmount.Description);

            sale.RuleFor(s => s)
                .Must(s => s.DueAmount <= 0 || s.CustomerId.HasValue || !string.IsNullOrWhiteSpace(s.CustomerPhone))
                .WithErrorCode(Errors.Sale.CustomerIdentityRequiredForDue.Code)
                .WithMessage(Errors.Sale.CustomerIdentityRequiredForDue.Description);

            sale.RuleFor(s => s)
                .Must(s => AmountsMatch(s.PaidAmount, s.DueAmount, s.TotalAmount))
                .WithErrorCode(Errors.Sale.PaidAndDueAmountMismatch.Code)
                .WithMessage(Errors.Sale.PaidAndDueAmountMismatch.Description);

            sale.RuleForEach(s => s.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.Barcode)
                    .NotEmpty()
                    .WithErrorCode("Sale.BarcodeRequired")
                    .WithMessage("Barcode is required.");

                item.RuleFor(i => i.BatchNumber)
                    .NotEmpty()
                    .WithErrorCode("Sale.BatchNumberRequired")
                    .WithMessage("Batch number is required.");

                item.RuleFor(i => i.ItemName)
                    .NotEmpty()
                    .WithErrorCode("Sale.ItemNameRequired")
                    .WithMessage("Item name is required.");

                item.RuleFor(i => i.InventoryBatchId)
                    .NotEmpty()
                    .WithErrorCode("Sale.InventoryBatchIdRequired")
                    .WithMessage("Inventory batch id is required.");

                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0)
                    .WithErrorCode("Sale.QuantityMustBePositive")
                    .WithMessage("Quantity must be greater than zero.");

                item.RuleFor(i => i.PreTaxAmountBeforeDiscount)
                    .GreaterThanOrEqualTo(0);

                item.RuleFor(i => i.ItemDiscountAmount)
                    .GreaterThanOrEqualTo(0);

                item.RuleFor(i => i.SaleDiscountAmount)
                    .GreaterThanOrEqualTo(0);

                item.RuleFor(i => i.TaxableAmount)
                    .GreaterThanOrEqualTo(0);

                item.RuleFor(i => i.TaxAmount)
                    .GreaterThanOrEqualTo(0);

                item.RuleFor(i => i.TotalAmount)
                    .GreaterThanOrEqualTo(0);
            });
        });
    }

    private static bool AmountsMatch(decimal paidAmount, decimal dueAmount, decimal totalAmount) =>
        decimal.Round(paidAmount + dueAmount, 2, MidpointRounding.AwayFromZero) ==
        decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
}
