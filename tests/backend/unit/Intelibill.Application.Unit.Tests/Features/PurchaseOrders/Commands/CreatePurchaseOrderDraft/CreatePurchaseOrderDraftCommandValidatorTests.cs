using FluentValidation.TestHelper;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;

public class CreatePurchaseOrderDraftCommandValidatorTests
{
    private readonly CreatePurchaseOrderDraftCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenLineDescriptionEmpty_ReturnsError()
    {
        var command = new CreatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            [new CreatePurchaseOrderLineInput("  ", 1, 10m)]);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Lines[0].Description");
    }

    [Fact]
    public void Validate_WhenExpectedQuantityIsNotPositive_ReturnsError()
    {
        var command = new CreatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            [new CreatePurchaseOrderLineInput("Item", 0, 10m)]);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Lines[0].ExpectedQuantity");
    }

    [Fact]
    public void Validate_WhenUnitCostIsNegative_ReturnsError()
    {
        var command = new CreatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            [new CreatePurchaseOrderLineInput("Item", 1, -1m)]);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Lines[0].UnitCost");
    }

    [Fact]
    public void Validate_WhenValidCommand_ReturnsNoErrors()
    {
        var command = new CreatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            [new CreatePurchaseOrderLineInput("Item", 1, 0m)]);

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenSupplierFieldsExceedLimits_ReturnsErrors()
    {
        var command = new CreatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new string('A', 201),
            new string('B', 121),
            [new CreatePurchaseOrderLineInput("Item", 1, 0m)]);

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.SupplierName);
        result.ShouldHaveValidationErrorFor(x => x.SupplierReference);
    }
}
