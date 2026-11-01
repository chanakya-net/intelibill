using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;

internal sealed class CorrectStockCountCommandValidator : AbstractValidator<CorrectStockCountCommand>
{
    public CorrectStockCountCommandValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ActiveShopId).NotEmpty();

        // Adjustments are stored to two places, so a count finer than that could not be posted.
        RuleFor(x => x.CountedQuantity)
            .Must(q => q >= 0 && decimal.Truncate(q * 100m) == q * 100m)
            .WithErrorCode(Errors.Inventory.StockCountQuantityInvalid.Code)
            .WithMessage(Errors.Inventory.StockCountQuantityInvalid.Description);

        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithErrorCode(Errors.Inventory.StockCountReasonRequired.Code)
            .WithMessage(Errors.Inventory.StockCountReasonRequired.Description)
            .Must(r => r is null || r.Trim().Length <= StockCount.MaxReasonLength)
            .WithMessage($"Reason cannot exceed {StockCount.MaxReasonLength} characters.");

        RuleFor(x => x.OperationKey)
            .MaximumLength(OperationReceipt.MaxKeyLength);
    }
}
