using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

public class UpdatePurchaseOrderDraftCommandValidatorTests
{
    private readonly UpdatePurchaseOrderDraftCommandValidator _validator = new();

    [Fact]
    public async Task Validate_WhenValid_HasNoErrors()
    {
        var command = new UpdatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            "Notes",
            [new UpdatePurchaseOrderLineInput(Guid.NewGuid(), "Widget", 10, 15m)]);

        var result = await _validator.ValidateAsync(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_WhenDescriptionEmpty_HasError()
    {
        var command = new UpdatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            "Notes",
            [new UpdatePurchaseOrderLineInput(Guid.NewGuid(), "", 10, 15m)]);

        var result = await _validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == Errors.PurchaseOrder.LineDescriptionRequired.Code);
    }

    [Fact]
    public async Task Validate_WhenQuantityZero_HasError()
    {
        var command = new UpdatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            "Notes",
            [new UpdatePurchaseOrderLineInput(Guid.NewGuid(), "Widget", 0, 15m)]);

        var result = await _validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == Errors.PurchaseOrder.InvalidLineQuantity.Code);
    }

    [Fact]
    public async Task Validate_WhenCostNegative_HasError()
    {
        var command = new UpdatePurchaseOrderDraftCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            "Notes",
            [new UpdatePurchaseOrderLineInput(Guid.NewGuid(), "Widget", 10, -1m)]);

        var result = await _validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == Errors.PurchaseOrder.InvalidLineUnitCost.Code);
    }
}
