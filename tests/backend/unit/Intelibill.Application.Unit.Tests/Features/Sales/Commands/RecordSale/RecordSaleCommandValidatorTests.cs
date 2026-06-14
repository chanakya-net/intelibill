using FluentValidation.TestHelper;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSale;

public class RecordSaleCommandValidatorTests
{
    private readonly RecordSaleCommandValidator _validator = new();

    private static RecordSaleCommand ValidCommand(IReadOnlyList<RecordSaleItemCommand>? items = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"sale-{Guid.NewGuid():N}",
            null,
            "Ravi Kumar",
            "+919876543210",
            PaymentMethod.Cash,
            500m,
            0m,
            items ?? [ValidItem()]);

    private static RecordSaleItemCommand ValidItem() =>
        new("BARCODE-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, Guid.NewGuid());

    [Fact]
    public void Validate_WhenItemsEmpty_ReturnsError()
    {
        var command = ValidCommand([]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_WhenIdempotencyKeyMissing_ReturnsError()
    {
        var command = ValidCommand() with { IdempotencyKey = "   " };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validate_WhenItemBarcodeEmpty_ReturnsError()
    {
        var command = ValidCommand([new("", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, Guid.NewGuid())]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Barcode");
    }

    [Fact]
    public void Validate_WhenItemBatchNumberEmpty_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "", "Rice", 5m, 80m, 100m, 120m, 18m, false, Guid.NewGuid())]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].BatchNumber");
    }

    [Fact]
    public void Validate_WhenServiceLineBatchNumberEmpty_DoesNotReturnError()
    {
        var command = ValidCommand(
        [
            new(
                "SVC-001",
                "",
                "Consulting",
                1m,
                0m,
                500m,
                500m,
                18m,
                false,
                Guid.Empty,
                new InstantDiscount(InstantDiscountType.None, 0m),
                LineType: SaleLineType.Service,
                ServiceId: Guid.NewGuid()),
        ]);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor("Items[0].BatchNumber");
    }

    [Fact]
    public void Validate_WhenInventoryBatchIdEmpty_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, Guid.Empty)]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].InventoryBatchId");
    }

    [Fact]
    public void Validate_WhenQuantityIsZero_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", 0m, 80m, 100m, 120m, 18m, false, Guid.NewGuid())]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WhenQuantityIsNegative_ReturnsError()
    {
        var command = ValidCommand([new("BC-001", "B-01", "Rice", -1m, 80m, 100m, 120m, 18m, false, Guid.NewGuid())]);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }


    [Fact]
    public void Validate_WhenValid_NoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenSaleDiscountPercentageOverHundred_ReturnsError()
    {
        var command = ValidCommand() with { SaleDiscount = new(InstantDiscountType.Percentage, 150m) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.SaleDiscount);
    }

    [Fact]
    public void Validate_WhenMultipleCreditNoteRedemptionsMatchAppliedAmount_Succeeds()
    {
        var command = ValidCommand() with
        {
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions =
            [
                new CreditNoteRedemptionCommand("CN-001", 50m),
                new CreditNoteRedemptionCommand("CN-002", 50m),
            ],
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenCreditNoteSplitMismatch_ReturnsError()
    {
        var command = ValidCommand() with
        {
            CreditNoteAppliedAmount = 100m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand("CN-001", 90m)],
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Validate_WhenCreditNoteAppliedAmountHasNoRedemption_ReturnsError()
    {
        var command = ValidCommand() with
        {
            CreditNoteAppliedAmount = 100m,
        };

        var result = _validator.TestValidate(command);

        Assert.Contains(result.Errors, error => error.ErrorCode == Errors.Sale.CreditNoteRedemptionsSplitMismatch.Code);
    }

    [Fact]
    public void Validate_WhenZeroAppliedAmountWithRedemptions_ReturnsError()
    {
        var command = ValidCommand() with
        {
            CreditNoteAppliedAmount = 0m,
            CreditNoteRedemptions = [new CreditNoteRedemptionCommand("CN-001", 50m)],
        };

        var result = _validator.TestValidate(command);

        Assert.Contains(result.Errors, error => error.ErrorCode == Errors.Sale.CreditNoteRedemptionsSplitMismatch.Code);
    }

    [Fact]
    public void Validate_WhenItemDiscountFlatNegative_ReturnsError()
    {
        var command = ValidCommand([ValidItem() with { ItemDiscount = new(InstantDiscountType.Flat, -1m) }]);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Items[0].ItemDiscount");
    }

    [Fact]
    public void Validate_WhenCreditHasNoDue_ReturnsError()
    {
        var command = ValidCommand() with
        {
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 500m,
            DueAmount = 0m,
            CustomerPhone = "+919876543210",
        };

        var result = _validator.TestValidate(command);

        Assert.Contains(result.Errors, e => e.ErrorCode == "Sale.CreditRequiresDueAmount");
    }

    [Fact]
    public void Validate_WhenCreditNoteCodesDuplicate_ReturnsError()
    {
        var command = ValidCommand() with
        {
            CreditNoteRedemptions =
            [
                new RecordSaleCreditNoteRedemptionCommand("CN-001", 60m),
                new RecordSaleCreditNoteRedemptionCommand("CN-001", 40m),
            ],
        };

        var result = _validator.TestValidate(command);

        Assert.Contains(result.Errors, e => e.ErrorCode == Errors.Sale.CreditNoteRedemptionDuplicateCode.Code);
    }

    [Fact]
    public void Validate_WhenDueWithoutCustomerIdentity_ReturnsError()
    {
        var command = ValidCommand() with
        {
            PaidAmount = 300m,
            DueAmount = 200m,
            CustomerId = null,
            CustomerPhone = null,
        };

        var result = _validator.TestValidate(command);

        Assert.Contains(result.Errors, e => e.ErrorCode == "Sale.CustomerIdentityRequiredForDue");
    }

}
