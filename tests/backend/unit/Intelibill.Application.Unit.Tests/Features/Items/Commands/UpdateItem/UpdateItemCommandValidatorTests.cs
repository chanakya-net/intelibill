using FluentValidation.TestHelper;
using Intelibill.Application.Features.Items.Commands.UpdateItem;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.UpdateItem;

public class UpdateItemCommandValidatorTests
{
    private readonly UpdateItemCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenNameIsEmpty_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "",
            Barcode: "123",
            Description: null,
            Uom: "kg",
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WhenBarcodeIsEmpty_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "",
            Description: null,
            Uom: "kg",
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Barcode);
    }

    [Fact]
    public void Validate_WhenUomIsEmpty_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "123",
            Description: null,
            Uom: "",
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Uom);
    }

    [Fact]
    public void Validate_WhenNameExceedsMaxLength_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: new string('a', 181),
            Barcode: "123",
            Description: null,
            Uom: "kg",
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WhenBarcodeExceedsMaxLength_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: new string('a', 129),
            Description: null,
            Uom: "kg",
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Barcode);
    }

    [Fact]
    public void Validate_WhenDescriptionExceedsMaxLength_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "123",
            Description: new string('a', 1001),
            Uom: "kg",
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Validate_WhenUomExceedsMaxLength_ReturnsError()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "123",
            Description: null,
            Uom: new string('a', 33),
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Uom);
    }

    [Fact]
    public void Validate_WhenValid_NoErrors()
    {
        var command = new UpdateItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "123",
            Description: "Premium quality rice",
            Uom: "kg",
            HsnCode: "10063090",
            DefaultTaxRatePercent: 5m);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("ABC123")]
    [InlineData("123456789")]
    public void Validate_WhenHsnCodeInvalid_ReturnsError(string hsnCode)
    {
        var command = CreateValidCommand() with { HsnCode = hsnCode };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.HsnCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_WhenDefaultTaxRatePercentOutOfRange_ReturnsError(decimal taxRate)
    {
        var command = CreateValidCommand() with { DefaultTaxRatePercent = taxRate };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DefaultTaxRatePercent);
    }

    private static UpdateItemCommand CreateValidCommand() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "123",
            Description: "Premium quality rice",
            Uom: "kg",
            HsnCode: "10063090",
            DefaultTaxRatePercent: 5m);
}
