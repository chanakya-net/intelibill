using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandValidator : AbstractValidator<RecordSaleCommand>
{
    public RecordSaleCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .Must(key => !string.IsNullOrWhiteSpace(key))
            .WithErrorCode(Errors.Sale.IdempotencyKeyRequired.Code)
            .WithMessage(Errors.Sale.IdempotencyKeyRequired.Description);

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(128)
            .WithErrorCode(Errors.Sale.IdempotencyKeyTooLong.Code)
            .WithMessage(Errors.Sale.IdempotencyKeyTooLong.Description);

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
                .When(i => i.LineType == SaleLineType.Goods)
                .WithErrorCode("Sale.BatchNumberRequired")
                .WithMessage("Batch number is required.");

            item.RuleFor(i => i.InventoryBatchId)
                .NotEmpty()
                .When(i => i.LineType == SaleLineType.Goods)
                .WithErrorCode("Sale.InventoryBatchIdRequired")
                .WithMessage("Inventory batch id is required.");

            item.RuleFor(i => i.ServiceId)
                .NotEmpty()
                .When(i => i.LineType == SaleLineType.Service)
                .WithErrorCode(Errors.Sale.ServiceNotFound.Code)
                .WithMessage(Errors.Sale.ServiceNotFound.Description);

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithErrorCode("Sale.QuantityMustBePositive")
                .WithMessage("Quantity must be greater than zero.");

            item.RuleFor(i => i.ItemDiscount)
                .Must(IsValidDiscount)
                .WithErrorCode(Errors.Sale.InvalidSaleDiscount.Code)
                .WithMessage(Errors.Sale.InvalidSaleDiscount.Description);

            item.RuleFor(i => i.ItemDiscount)
                .Must(d => d is null || d.Type == InstantDiscountType.None)
                .When(i => i.LineType == SaleLineType.Service)
                .WithErrorCode(Errors.Sale.ServiceDiscountNotSupported.Code)
                .WithMessage(Errors.Sale.ServiceDiscountNotSupported.Description);
        });

        RuleFor(x => x.SaleDiscount)
            .Must(IsValidDiscount)
            .WithErrorCode(Errors.Sale.InvalidSaleDiscount.Code)
            .WithMessage(Errors.Sale.InvalidSaleDiscount.Description);

        RuleFor(x => x.PaidAmount)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(Errors.Sale.PaidAmountInvalid.Code)
            .WithMessage(Errors.Sale.PaidAmountInvalid.Description);

        RuleFor(x => x.DueAmount)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(Errors.Sale.DueAmountInvalid.Code)
            .WithMessage(Errors.Sale.DueAmountInvalid.Description);

        RuleFor(x => x.CreditNoteAppliedAmount)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(Errors.Sale.CreditNoteAppliedAmountInvalid.Code)
            .WithMessage(Errors.Sale.CreditNoteAppliedAmountInvalid.Description);

        RuleFor(x => x.CreditNoteRedemptions)
            .Must(redemptions => redemptions is null || redemptions.Count <= 1)
            .WithErrorCode(Errors.Sale.CreditNoteRedemptionsLimitExceeded.Code)
            .WithMessage(Errors.Sale.CreditNoteRedemptionsLimitExceeded.Description);

        RuleForEach(x => x.CreditNoteRedemptions).ChildRules(redemption =>
        {
            redemption.RuleFor(r => r.Code)
                .NotEmpty()
                .WithErrorCode("Sale.CreditNoteRedemptionCodeRequired")
                .WithMessage("Credit note code is required.");

            redemption.RuleFor(r => r.Amount)
                .GreaterThan(0)
                .WithErrorCode("Sale.CreditNoteRedemptionAmountMustBePositive")
                .WithMessage("Credit note redemption amount must be greater than zero.");
        });

        RuleFor(x => x)
            .Must(x => x.CreditNoteRedemptions is null || x.CreditNoteRedemptions.Count == 0 || x.CreditNoteRedemptions.Sum(r => r.Amount) == x.CreditNoteAppliedAmount)
            .WithErrorCode(Errors.Sale.CreditNoteRedemptionsSplitMismatch.Code)
            .WithMessage(Errors.Sale.CreditNoteRedemptionsSplitMismatch.Description);

        RuleFor(x => x)
            .Must(x => x.PaymentMethod != PaymentMethod.Credit || x.DueAmount > 0)
            .WithErrorCode(Errors.Sale.CreditRequiresDueAmount.Code)
            .WithMessage(Errors.Sale.CreditRequiresDueAmount.Description);

        RuleFor(x => x)
            .Must(x => x.DueAmount <= 0 || x.CustomerId.HasValue || !string.IsNullOrWhiteSpace(x.CustomerPhone))
            .WithErrorCode(Errors.Sale.CustomerIdentityRequiredForDue.Code)
            .WithMessage(Errors.Sale.CustomerIdentityRequiredForDue.Description);
    }

    private static bool IsValidDiscount(InstantDiscount? discount) =>
        discount is null || discount.Type switch
        {
            InstantDiscountType.None => discount.Value == 0m,
            InstantDiscountType.Percentage => discount.Value > 0m && discount.Value <= 100m,
            InstantDiscountType.Flat => discount.Value > 0m,
            _ => false,
        };
}
