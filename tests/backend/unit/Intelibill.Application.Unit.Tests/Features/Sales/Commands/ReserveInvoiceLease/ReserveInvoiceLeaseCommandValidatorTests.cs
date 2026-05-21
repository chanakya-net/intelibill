using FluentValidation.TestHelper;
using Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.ReserveInvoiceLease;

public class ReserveInvoiceLeaseCommandValidatorTests
{
    private readonly ReserveInvoiceLeaseCommandValidator _validator = new();

    private static ReserveInvoiceLeaseCommand ValidCommand(int? blockSize = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "device-1", blockSize);

    [Fact]
    public void Validate_WhenDeviceIdMissing_ReturnsError()
    {
        var command = ValidCommand() with { DeviceId = " " };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DeviceId);
    }

    [Fact]
    public void Validate_WhenBlockSizeZero_ReturnsError()
    {
        var command = ValidCommand(0);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.BlockSize);
    }

    [Fact]
    public void Validate_WhenBlockSizeTooLarge_ReturnsError()
    {
        var command = ValidCommand(2000);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.BlockSize);
    }

    [Fact]
    public void Validate_WhenValid_NoErrors()
    {
        var result = _validator.TestValidate(ValidCommand(200));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
