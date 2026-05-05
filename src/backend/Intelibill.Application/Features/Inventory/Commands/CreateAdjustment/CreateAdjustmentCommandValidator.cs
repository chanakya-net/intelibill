using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;

internal sealed class CreateAdjustmentCommandValidator : AbstractValidator<CreateAdjustmentCommand>
{
    public CreateAdjustmentCommandValidator()
    {
        RuleFor(x => x.BatchId)
            .NotEmpty();

        RuleFor(x => x.ActorUserId)
            .NotEmpty();

        RuleFor(x => x.ActiveShopId)
            .NotEmpty();

        RuleFor(x => x.Direction)
            .IsInEnum();

        RuleFor(x => x.Reason)
            .IsInEnum();

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithErrorCode(Errors.Inventory.QuantityMustBePositive.Code)
            .Must(NotHaveMoreThanTwoDecimalPlaces)
            .WithErrorCode(Errors.Inventory.AdjustmentQuantityScaleInvalid.Code)
            .WithMessage(Errors.Inventory.AdjustmentQuantityScaleInvalid.Description);

        RuleFor(x => x.PerformedAt)
            .Must(performedAt => !performedAt.HasValue || performedAt.Value <= DateTimeOffset.UtcNow)
            .WithErrorCode(Errors.Inventory.AdjustmentPerformedAtInFuture.Code)
            .WithMessage(Errors.Inventory.AdjustmentPerformedAtInFuture.Description);

        RuleFor(x => x)
            .Must(x => IsReasonAllowedForDirection(x.Direction, x.Reason))
            .WithErrorCode(Errors.Inventory.AdjustmentReasonDirectionMismatch.Code)
            .WithMessage(Errors.Inventory.AdjustmentReasonDirectionMismatch.Description);

        RuleFor(x => x.Notes)
            .NotEmpty()
            .When(x => x.Reason is InventoryAdjustmentReason.OtherLoss or InventoryAdjustmentReason.OtherGain)
            .WithErrorCode(Errors.Inventory.AdjustmentNotesRequired.Code)
            .WithMessage(Errors.Inventory.AdjustmentNotesRequired.Description);
    }

    private static bool NotHaveMoreThanTwoDecimalPlaces(decimal value) =>
        decimal.Truncate(value * 100m) == value * 100m;

    private static bool IsReasonAllowedForDirection(InventoryAdjustmentDirection direction, InventoryAdjustmentReason reason)
    {
        return direction switch
        {
            InventoryAdjustmentDirection.Decrease => reason is InventoryAdjustmentReason.Damaged
                or InventoryAdjustmentReason.Expired
                or InventoryAdjustmentReason.Stolen
                or InventoryAdjustmentReason.MissingLost
                or InventoryAdjustmentReason.StockCountCorrection
                or InventoryAdjustmentReason.OtherLoss,
            InventoryAdjustmentDirection.Increase => reason is InventoryAdjustmentReason.FoundStock
                or InventoryAdjustmentReason.StockCountCorrection
                or InventoryAdjustmentReason.ReturnRestockCorrection
                or InventoryAdjustmentReason.OtherGain,
            _ => false,
        };
    }
}
