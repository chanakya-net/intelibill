using FluentValidation.TestHelper;
using Intelibill.Application.Features.Items.Commands.AddItem;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.AddItem;

public class AddItemCommandValidatorTests
{
    private readonly AddItemCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenBarcodeIsQrLikeString_ReturnsNoErrors()
    {
        var command = new AddItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: CreateQrLikeBarcode(),
            Description: "Premium quality rice",
            Uom: "kg",
            IsActive: true,
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Barcode);
    }

    [Fact]
    public void Validate_WhenBarcodeIsEmpty_ReturnsError()
    {
        var command = new AddItemCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "",
            Description: null,
            Uom: "kg",
            IsActive: true,
            HsnCode: null,
            DefaultTaxRatePercent: 0m);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Barcode);
    }

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('A', 24)}";

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

    [Fact]
    public void Validate_WhenScaleOutOfRange_ReturnsQuantityScaleInvalid()
    {
        var result = _validator.TestValidate(CreateValidCommand() with { Uom = "Bundle", QuantityScale = 4 });

        Assert.Contains(result.Errors, e => e.ErrorCode == "Item.QuantityScaleInvalid");
    }

    [Fact]
    public void Validate_WhenScaleContradictsCatalogUnit_ReturnsQuantityScaleMismatch()
    {
        var result = _validator.TestValidate(CreateValidCommand() with { Uom = "kg", QuantityScale = 0 });

        Assert.Contains(result.Errors, e => e.ErrorCode == "Item.QuantityScaleMismatch");
    }

    [Fact]
    public void Validate_WhenCatalogUnitScaleMatches_IsValid()
    {
        var result = _validator.TestValidate(CreateValidCommand() with { Uom = "kg", QuantityScale = 3 });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenCustomUnitHasScale_IsValid()
    {
        var result = _validator.TestValidate(CreateValidCommand() with { Uom = "Bundle", QuantityScale = 2 });

        result.ShouldNotHaveAnyValidationErrors();
    }

    private static AddItemCommand CreateValidCommand() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Name: "Rice",
            Barcode: "123",
            Description: null,
            Uom: "kg",
            IsActive: true,
            HsnCode: "10063090",
            DefaultTaxRatePercent: 5m);
}
