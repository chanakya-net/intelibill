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
            IsActive: true);

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
            IsActive: true);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Barcode);
    }

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('A', 24)}";
}
