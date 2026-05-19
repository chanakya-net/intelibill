using FluentValidation.TestHelper;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Validators;

public class AddInventoryCommandValidatorTests
{
    private readonly AddInventoryCommandValidator _v = new();

    private static AddInventoryCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Item", "BARCODE-001", null, null,
            "kg", "BATCH-001", 10m, 80m, 120m, 100m, 18m, false, null, null, null, null, null, null);

    [Fact] public void Validate_WhenItemNameEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { ItemName = "" }).ShouldHaveValidationErrorFor(x => x.ItemName);

    [Fact] public void Validate_WhenBarcodeEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Barcode = "" }).ShouldHaveValidationErrorFor(x => x.Barcode);

    [Fact] public void Validate_WhenUomEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { Uom = "" }).ShouldHaveValidationErrorFor(x => x.Uom);

    [Fact] public void Validate_WhenBatchNumberEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { BatchNumber = "" }).ShouldHaveValidationErrorFor(x => x.BatchNumber);

    [Fact] public void Validate_WhenQuantityZero_ReturnsError() =>
        _v.TestValidate(Valid() with { Quantity = 0m }).ShouldHaveValidationErrorFor(x => x.Quantity);

    [Fact] public void Validate_WhenSalesPriceExceedsMrp_ReturnsError() =>
        _v.TestValidate(Valid() with { SalesPrice = 200m, Mrp = 100m }).ShouldHaveValidationErrorFor(x => x.SalesPrice);

    [Fact] public void Validate_WhenTaxRateOutOfRange_ReturnsError() =>
        _v.TestValidate(Valid() with { TaxRatePercent = 101m }).ShouldHaveValidationErrorFor(x => x.TaxRatePercent);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}

public class UpdateInventoryBatchCommandValidatorTests
{
    private readonly UpdateInventoryBatchCommandValidator _v = new();

    private static UpdateInventoryBatchCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BATCH-NEW", 10m, 80m, 120m, 100m, 18m, false, null, null, null, null, null);

    [Fact] public void Validate_WhenBatchIdEmpty_ReturnsError() =>
        _v.TestValidate(Valid() with { BatchId = Guid.Empty }).ShouldHaveValidationErrorFor(x => x.BatchId);

    [Fact] public void Validate_WhenBatchNumberTooLong_ReturnsError() =>
        _v.TestValidate(Valid() with { NewBatchNumber = new string('x', 81) }).ShouldHaveValidationErrorFor(x => x.NewBatchNumber);

    [Fact] public void Validate_WhenQuantityNegative_ReturnsError() =>
        _v.TestValidate(Valid() with { Quantity = -1m }).ShouldHaveValidationErrorFor(x => x.Quantity);

    [Fact] public void Validate_WhenSalesPriceExceedsMrp_ReturnsError() =>
        _v.TestValidate(Valid() with { SalesPrice = 200m, Mrp = 100m }).ShouldHaveValidationErrorFor(x => x.SalesPrice);

    [Fact] public void Validate_WhenTaxRateOutOfRange_ReturnsError() =>
        _v.TestValidate(Valid() with { TaxRatePercent = 101m }).ShouldHaveValidationErrorFor(x => x.TaxRatePercent);

    [Fact] public void Validate_WhenValid_NoErrors() =>
        _v.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
}
