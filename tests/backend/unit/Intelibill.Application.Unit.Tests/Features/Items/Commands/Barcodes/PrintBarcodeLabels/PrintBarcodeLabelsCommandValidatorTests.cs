using FluentValidation.TestHelper;
using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Application.Features.Items.Barcodes.PrintBarcodeLabels;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.Barcodes.PrintBarcodeLabels;

public sealed class PrintBarcodeLabelsCommandValidatorTests
{
    private readonly PrintBarcodeLabelsCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenItemsEmpty_ReturnsValidationError()
    {
        var command = new PrintBarcodeLabelsCommand(Guid.NewGuid(), Guid.NewGuid(), []);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenQuantityIsNotPositive_ReturnsValidationError()
    {
        var command = new PrintBarcodeLabelsCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [new PrintBarcodeLabelItemRequest(Guid.NewGuid(), 0, null)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WhenTotalQuantityExceedsMax_ReturnsValidationError()
    {
        var command = new PrintBarcodeLabelsCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [
                new PrintBarcodeLabelItemRequest(Guid.NewGuid(), 300, null),
                new PrintBarcodeLabelItemRequest(Guid.NewGuid(), 201, null),
            ]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenPayloadValid_ReturnsNoErrors()
    {
        var command = new PrintBarcodeLabelsCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [
                new PrintBarcodeLabelItemRequest(Guid.NewGuid(), 2, null),
                new PrintBarcodeLabelItemRequest(Guid.NewGuid(), 1, Guid.NewGuid()),
            ]);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
