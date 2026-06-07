using FluentValidation.TestHelper;
using Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

public class CancelPurchaseOrderCommandValidatorTests
{
    private readonly CancelPurchaseOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenReasonIsBlank_ReturnsError()
    {
        var command = new CancelPurchaseOrderCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "   ");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Validate_WhenReasonExceedsLimit_ReturnsError()
    {
        var command = new CancelPurchaseOrderCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('A', 501));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Validate_WhenReasonIsWithinLimit_ReturnsNoErrors()
    {
        var command = new CancelPurchaseOrderCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('A', 500));

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
