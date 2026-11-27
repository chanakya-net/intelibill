using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;

internal sealed class CorrectReceiptCostCommandValidator : AbstractValidator<CorrectReceiptCostCommand>
{
    public CorrectReceiptCostCommandValidator()
    {
        RuleFor(x => x.ReceiptValueId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ActiveShopId).NotEmpty();

        RuleFor(x => x.RevisedTotalCost)
            .Must(c => c >= 0 && c <= PurchaseReceiptValue.MaxTotalCost && decimal.Round(c, 2) == c)
            .WithErrorCode(Errors.PurchaseReceipt.TotalCostInvalid.Code)
            .WithMessage(Errors.PurchaseReceipt.TotalCostInvalid.Description);

        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithErrorCode(Errors.PurchaseReceipt.ReasonRequired.Code)
            .WithMessage(Errors.PurchaseReceipt.ReasonRequired.Description)
            .Must(r => r is null || r.Trim().Length <= PurchaseReceiptCostAdjustment.MaxReasonLength)
            .WithErrorCode(Errors.PurchaseReceipt.ReasonTooLong.Code)
            .WithMessage(Errors.PurchaseReceipt.ReasonTooLong.Description);

        RuleFor(x => x.OperationKey)
            .MaximumLength(OperationReceipt.MaxKeyLength);
    }
}
