using FluentValidation.TestHelper;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSale;

public class RecordSaleCommandValidatorTests
{
    private readonly RecordSaleCommandValidator _validator = new();

    private static RecordSaleCommand ValidCommand(IReadOnlyList<RecordSaleItemCommand>? items = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Ravi Kumar",
            "+919876543210",
            PaymentMethod.Cash,
            items ?? [ValidItem()]);

    private static RecordSaleItemCommand ValidItem() =>
        new("BARCODE-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false);

    [Fact]
    public void Validate_WhenItemsEmpty_ReturnsError()
    {
        var command = ValidCommand([]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenItemBarcodeEmpty_ReturnsError()
    {
        var command = ValidCommand([new("", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Barcode");
    }

    [Fact]
    public void Validate_WhenItemBatchNumberEmpty_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "", "Rice", 5m, 80m, 100m, 120m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].BatchNumber");
    }

    [Fact]
    public void Validate_WhenQuantityIsZero_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 0m, 80m, 100m, 120m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WhenQuantityIsNegative_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", -1m, 80m, 100m, 120m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WhenCostPriceIsNegative_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 5m, -1m, 100m, 120m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].CostPrice");
    }

    [Fact]
    public void Validate_WhenTaxRateExceedsHundred_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 101m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].TaxRatePercent");
    }

    [Fact]
    public void Validate_WhenTaxRateIsNegative_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, -1m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].TaxRatePercent");
    }

    [Fact]
    public void Validate_WhenValid_NoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenSalesPriceIsNegative_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 5m, 80m, -1m, 120m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].SalesPrice");
    }

    [Fact]
    public void Validate_WhenMrpIsNegative_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 5m, 80m, 100m, -1m, 18m, false)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Mrp");
    }
}
