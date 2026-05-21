using FluentValidation.TestHelper;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.SyncOfflineSales;

public class SyncOfflineSalesCommandValidatorTests
{
    private readonly SyncOfflineSalesCommandValidator _validator = new();

    private static OfflineSaleSyncLineCommand ValidLine() =>
        new(
            "BC-001",
            "B-01",
            "Rice",
            1m,
            80m,
            100m,
            120m,
            18m,
            false,
            Guid.NewGuid(),
            100m,
            0m,
            0m,
            100m,
            18m,
            118m,
            null,
            null,
            InstantDiscountType.None,
            0m,
            null);

    private static OfflineSaleSyncCommand ValidSale() =>
        new(
            $"offline-{Guid.NewGuid():N}",
            "INV-2025-26-000001",
            DateTimeOffset.UtcNow,
            null,
            "Ravi Kumar",
            "+919876543210",
            PaymentMethod.Cash,
            118m,
            0m,
            100m,
            118m,
            0m,
            18m,
            118m,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            [ValidLine()]);

    private static SyncOfflineSalesCommand ValidCommand(IReadOnlyList<OfflineSaleSyncCommand>? sales = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "device-1", sales ?? [ValidSale()]);

    [Fact]
    public void Validate_WhenDeviceIdMissing_ReturnsError()
    {
        var command = ValidCommand() with { DeviceId = "   " };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DeviceId);
    }

    [Fact]
    public void Validate_WhenBatchLimitExceeded_ReturnsError()
    {
        var sales = Enumerable.Range(0, 51).Select(_ => ValidSale()).ToList();
        var command = ValidCommand(sales);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Sales);
    }

    [Fact]
    public void Validate_WhenValid_NoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenSalePayloadInvalid_DoesNotRejectEnvelope()
    {
        var invalidSale = ValidSale() with
        {
            ClientSaleId = "",
            InvoiceNumber = "",
            Items = [],
        };

        var result = _validator.TestValidate(ValidCommand([invalidSale]));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
