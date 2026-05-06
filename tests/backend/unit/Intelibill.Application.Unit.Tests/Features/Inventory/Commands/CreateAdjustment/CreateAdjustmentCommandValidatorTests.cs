using FluentValidation.TestHelper;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.CreateAdjustment;

public sealed class CreateAdjustmentCommandValidatorTests
{
    private readonly CreateAdjustmentCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenQuantityHasMoreThanTwoDecimalPlaces_ReturnsError()
    {
        var result = _validator.TestValidate(Valid() with { Quantity = 1.123m });

        result.ShouldHaveValidationErrorFor(x => x.Quantity)
            .WithErrorCode(Errors.Inventory.AdjustmentQuantityScaleInvalid.Code);
    }

    [Fact]
    public void Validate_WhenPerformedAtIsFuture_ReturnsError()
    {
        var result = _validator.TestValidate(Valid() with { PerformedAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        result.ShouldHaveValidationErrorFor(x => x.PerformedAt)
            .WithErrorCode(Errors.Inventory.AdjustmentPerformedAtInFuture.Code);
    }

    [Fact]
    public void Validate_WhenReasonDoesNotMatchDirection_ReturnsError()
    {
        var result = _validator.TestValidate(Valid() with { Direction = InventoryAdjustmentDirection.Increase, Reason = InventoryAdjustmentReason.Damaged });

        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorCode(Errors.Inventory.AdjustmentReasonDirectionMismatch.Code);
    }

    [Fact]
    public void Validate_WhenOtherLossHasNoNotes_ReturnsError()
    {
        var result = _validator.TestValidate(Valid() with { Reason = InventoryAdjustmentReason.OtherLoss, Notes = null });

        result.ShouldHaveValidationErrorFor(x => x.Notes)
            .WithErrorCode(Errors.Inventory.AdjustmentNotesRequired.Code);
    }

    [Fact]
    public void Validate_WhenValidBackdatedCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(Valid() with { PerformedAt = DateTimeOffset.UtcNow.AddDays(-2) });

        result.ShouldNotHaveAnyValidationErrors();
    }

    private static CreateAdjustmentCommand Valid() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1.5m,
            null,
            "Damaged");
}
