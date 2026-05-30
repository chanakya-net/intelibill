using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class StockTransactionTests
{
    [Fact]
    public void Create_InTransaction_WithPositiveQuantity_Succeeds()
    {
        var result = StockTransaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            StockTransactionType.In,
            quantity: 10m,
            referenceNumber: "  PO-11  ",
            notes: "  Received stock  ",
            performedAt: DateTimeOffset.UtcNow,
            performedBy: Guid.NewGuid(),
            createdBy: Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal("PO-11", result.Value.ReferenceNumber);
        Assert.Equal("Received stock", result.Value.Notes);
    }

    [Fact]
    public void Create_OutTransaction_WithPositiveQuantity_ReturnsValidationError()
    {
        var result = StockTransaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            StockTransactionType.Out,
            quantity: 2m,
            referenceNumber: null,
            notes: null,
            performedAt: DateTimeOffset.UtcNow,
            performedBy: Guid.NewGuid(),
            createdBy: Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("StockTransaction.QuantitySignInvalid", result.FirstError.Code);
    }

    [Fact]
    public void Create_AdjTransaction_WithNegativeQuantity_Succeeds()
    {
        var result = StockTransaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            StockTransactionType.Adj,
            quantity: -1m,
            referenceNumber: null,
            notes: null,
            performedAt: DateTimeOffset.UtcNow,
            performedBy: Guid.NewGuid(),
            createdBy: Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal(-1m, result.Value.Quantity);
    }
}
