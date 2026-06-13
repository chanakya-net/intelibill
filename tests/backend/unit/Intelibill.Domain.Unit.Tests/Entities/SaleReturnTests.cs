using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleReturnTests
{
    [Fact]
    public void Record_WithValidHeaderAndLines_ReturnsSaleReturn()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var processedAt = new DateTimeOffset(2026, 5, 4, 10, 15, 0, TimeSpan.Zero);
        var line = new SaleReturnLineInput(
            shopId,
            Guid.NewGuid(),
            1.5m,
            SaleReturnCondition.Restockable,
            80m,
            100m,
            18m,
            false,
            118m,
            118m,
            100m,
            18m,
            null);

        var result = SaleReturn.Record(
            shopId,
            saleId,
            "RET-20260504-ABC123EF",
            processedAt,
            actorUserId,
            notes: "  Damaged packet  ",
            totalRefundAmount: 118m,
            dueReductionAmount: 0m,
            payoutAmount: 118m,
            payoutDestination: ReturnPayoutDestination.Refund,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: 200m,
            customerBalanceAfter: 200m,
            [line]);

        Assert.False(result.IsError);

        var saleReturn = result.Value;
        Assert.Equal(shopId, saleReturn.ShopId);
        Assert.Equal(saleId, saleReturn.SaleId);
        Assert.Equal("RET-20260504-ABC123EF", saleReturn.ReturnNumber);
        Assert.Equal(processedAt, saleReturn.ProcessedAt);
        Assert.Equal(actorUserId, saleReturn.ProcessedBy);
        Assert.Equal("Damaged packet", saleReturn.Notes);
        Assert.Equal(118m, saleReturn.TotalRefundAmount);
        Assert.Equal(0m, saleReturn.DueReductionAmount);
        Assert.Equal(118m, saleReturn.PayoutAmount);
        Assert.Equal(ReturnPayoutDestination.Refund, saleReturn.PayoutDestination);
        Assert.Equal(100m, saleReturn.TotalTaxableAmount);
        Assert.Equal(18m, saleReturn.TotalTaxAmount);
        Assert.Equal(200m, saleReturn.CustomerBalanceBefore);
        Assert.Equal(200m, saleReturn.CustomerBalanceAfter);
        Assert.False(saleReturn.IsVoided);
        var createdLine = Assert.Single(saleReturn.Items);
        Assert.Equal(line.ShopId, createdLine.ShopId);
        Assert.Equal(line.SaleItemId, createdLine.SaleItemId);
        Assert.Equal(line.Quantity, createdLine.Quantity);
        Assert.Equal(line.Condition, createdLine.Condition);
        Assert.Equal(line.Notes, createdLine.Notes);
    }

    [Fact]
    public void Record_WithWastageLineWithoutNotes_ReturnsNoteRequired()
    {
        var result = SaleReturn.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: 118m,
            dueReductionAmount: 118m,
            payoutAmount: 0m,
            payoutDestination: null,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [
                new SaleReturnLineInput(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1m,
                    SaleReturnCondition.Wastage,
                    80m,
                    100m,
                    18m,
                    false,
                    118m,
                    118m,
                    100m,
                    18m,
                    null),
            ]);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.NoteRequired", result.FirstError.Code);
    }

    [Fact]
    public void Record_WithPartialRefundLineWithoutNotes_ReturnsNoteRequired()
    {
        var result = SaleReturn.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: 80m,
            dueReductionAmount: 80m,
            payoutAmount: 0m,
            payoutDestination: null,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [
                new SaleReturnLineInput(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1m,
                    SaleReturnCondition.Restockable,
                    80m,
                    100m,
                    18m,
                    false,
                    118m,
                    80m,
                    100m,
                    18m,
                    null),
            ]);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.NoteRequired", result.FirstError.Code);
    }

    [Fact]
    public void Record_WithZeroRefundLineWithoutNotes_ReturnsNoteRequired()
    {
        var result = SaleReturn.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: 0m,
            dueReductionAmount: 0m,
            payoutAmount: 0m,
            payoutDestination: null,
            totalTaxableAmount: 0m,
            totalTaxAmount: 0m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [
                new SaleReturnLineInput(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1m,
                    SaleReturnCondition.Restockable,
                    80m,
                    100m,
                    18m,
                    false,
                    118m,
                    0m,
                    0m,
                    0m,
                    null),
            ]);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.NoteRequired", result.FirstError.Code);
    }

    [Fact]
    public void Record_WithPayoutAmountAndNullDestination_ReturnsPayoutDestinationRequired()
    {
        var result = SaleReturn.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: "return",
            totalRefundAmount: 25m,
            dueReductionAmount: 0m,
            payoutAmount: 25m,
            payoutDestination: null,
            totalTaxableAmount: 20m,
            totalTaxAmount: 5m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [
                new SaleReturnLineInput(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1m,
                    SaleReturnCondition.Restockable,
                    15m,
                    20m,
                    25m,
                    false,
                    25m,
                    25m,
                    20m,
                    5m,
                    "ok"),
            ]);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.PayoutDestinationRequired", result.FirstError.Code);
    }

    [Theory]
    [InlineData(ReturnPayoutDestination.Refund)]
    [InlineData(ReturnPayoutDestination.CreditNote)]
    public void Record_WithPayoutAmountAndValidDestination_Succeeds(ReturnPayoutDestination destination)
    {
        var result = SaleReturn.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: "return",
            totalRefundAmount: 25m,
            dueReductionAmount: 0m,
            payoutAmount: 25m,
            payoutDestination: destination,
            totalTaxableAmount: 20m,
            totalTaxAmount: 5m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [
                new SaleReturnLineInput(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    1m,
                    SaleReturnCondition.Restockable,
                    15m,
                    20m,
                    25m,
                    false,
                    25m,
                    25m,
                    20m,
                    5m,
                    "ok"),
            ]);

        Assert.False(result.IsError);
        Assert.Equal(destination, result.Value.PayoutDestination);
    }

    [Fact]
    public void Record_WithValidHeader_SetsAuditAndRefundFields()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var processedBy = Guid.NewGuid();
        var processedAt = new DateTimeOffset(2026, 5, 4, 10, 15, 0, TimeSpan.Zero);
        var line = new SaleReturnLineInput(
            shopId,
            Guid.NewGuid(),
            1m,
            SaleReturnCondition.Restockable,
            80m,
            100m,
            18m,
            false,
            118m,
            118m,
            100m,
            18m,
            null);

        var result = SaleReturn.Record(
            shopId,
            saleId,
            "RET-20260504-ABC123EF",
            processedAt,
            processedBy,
            notes: "  Damaged packet  ",
            totalRefundAmount: 118m,
            dueReductionAmount: 50m,
            payoutAmount: 68m,
            payoutDestination: ReturnPayoutDestination.Refund,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: 200m,
            customerBalanceAfter: 150m,
            [line]);

        Assert.False(result.IsError);
        var saleReturn = result.Value;
        Assert.Equal(shopId, saleReturn.ShopId);
        Assert.Equal(saleId, saleReturn.SaleId);
        Assert.Equal("RET-20260504-ABC123EF", saleReturn.ReturnNumber);
        Assert.Equal(processedAt, saleReturn.ProcessedAt);
        Assert.Equal(processedBy, saleReturn.ProcessedBy);
        Assert.Equal("Damaged packet", saleReturn.Notes);
        Assert.Equal(118m, saleReturn.TotalRefundAmount);
        Assert.Equal(50m, saleReturn.DueReductionAmount);
        Assert.Equal(68m, saleReturn.PayoutAmount);
        Assert.Equal(100m, saleReturn.TotalTaxableAmount);
        Assert.Equal(18m, saleReturn.TotalTaxAmount);
        Assert.Equal(200m, saleReturn.CustomerBalanceBefore);
        Assert.Equal(150m, saleReturn.CustomerBalanceAfter);
        Assert.False(saleReturn.IsVoided);
        var createdLine = Assert.Single(saleReturn.Items);
        Assert.Equal(line.SaleItemId, createdLine.SaleItemId);
        Assert.Equal(line.Quantity, createdLine.Quantity);
        Assert.Equal(line.Condition, createdLine.Condition);
    }

    [Fact]
    public void Record_WithValidLine_SetsReturnLineFields()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var line = new SaleReturnLineInput(
            shopId,
            Guid.NewGuid(),
            2.5m,
            SaleReturnCondition.Wastage,
            80m,
            100m,
            18m,
            false,
            250m,
            200m,
            250m,
            45m,
            "  Torn label  ");

        var result = SaleReturn.Record(
            shopId,
            saleId,
            "RET-20260504-ABC123EF",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: "line",
            totalRefundAmount: 200m,
            dueReductionAmount: 0m,
            payoutAmount: 200m,
            payoutDestination: ReturnPayoutDestination.Refund,
            totalTaxableAmount: 250m,
            totalTaxAmount: 45m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [line]);

        Assert.False(result.IsError);
        var createdLine = Assert.Single(result.Value.Items);
        Assert.Equal(shopId, createdLine.ShopId);
        Assert.Equal(line.SaleItemId, createdLine.SaleItemId);
        Assert.Equal(2.5m, createdLine.Quantity);
        Assert.Equal(SaleReturnCondition.Wastage, createdLine.Condition);
        Assert.Equal(80m, createdLine.OriginalCostPrice);
        Assert.Equal(100m, createdLine.OriginalSalesPrice);
        Assert.Equal(18m, createdLine.OriginalTaxRatePercent);
        Assert.False(createdLine.OriginalIsPriceIncludingTax);
        Assert.Equal(250m, createdLine.MaxRefundAmount);
        Assert.Equal(200m, createdLine.ApprovedRefundAmount);
        Assert.Equal(250m, createdLine.TaxableAmount);
        Assert.Equal(45m, createdLine.TaxAmount);
        Assert.Equal("Torn label", createdLine.Notes);
    }

    [Fact]
    public void Void_WithReason_MarksReturnVoided()
    {
        var processedAt = new DateTimeOffset(2026, 5, 4, 10, 15, 0, TimeSpan.Zero);
        var voidedAt = processedAt.AddHours(1);
        var voidedBy = Guid.NewGuid();
        var saleReturn = SaleReturn.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            processedAt,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: 0m,
            dueReductionAmount: 0m,
            payoutAmount: 0m,
            payoutDestination: null,
            totalTaxableAmount: 0m,
            totalTaxAmount: 0m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            []).Value;

        var result = saleReturn.Void(voidedAt, voidedBy, "  Entry mistake  ");

        Assert.False(result.IsError);
        Assert.True(saleReturn.IsVoided);
        Assert.Equal(voidedAt, saleReturn.VoidedAt);
        Assert.Equal(voidedBy, saleReturn.VoidedBy);
        Assert.Equal("Entry mistake", saleReturn.VoidReason);
    }
}
