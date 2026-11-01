using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;

internal sealed class CorrectStockCountCommandValidator : AbstractValidator<CorrectStockCountCommand>
{
    private const decimal MaxQuantity = 1_000_000_000_000_000m;

    public CorrectStockCountCommandValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ActiveShopId).NotEmpty();

        // Stock is stored as numeric(18,3): at most three places and fifteen integer digits.
        RuleFor(x => x.CountedQuantity)
            .Must(q => q >= 0 && q < MaxQuantity && decimal.Truncate(q * 1000m) == q * 1000m)
            .WithErrorCode(Errors.Inventory.StockCountQuantityInvalid.Code)
            .WithMessage(Errors.Inventory.StockCountQuantityInvalid.Description);

        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithErrorCode(Errors.Inventory.StockCountReasonRequired.Code)
            .WithMessage(Errors.Inventory.StockCountReasonRequired.Description)
            .Must(r => r is null || r.Trim().Length <= StockCount.MaxReasonLength)
            .WithErrorCode(Errors.Inventory.StockCountReasonTooLong.Code)
            .WithMessage(Errors.Inventory.StockCountReasonTooLong.Description);

        RuleFor(x => x.OperationKey)
            .MaximumLength(OperationReceipt.MaxKeyLength);
    }
}
