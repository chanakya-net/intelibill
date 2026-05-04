using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleReturnTests
{
    [Fact]
    public void Create_WithValidHeader_SetsAuditAndRefundFields()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var processedBy = Guid.NewGuid();
        var processedAt = new DateTimeOffset(2026, 5, 4, 10, 15, 0, TimeSpan.Zero);

        var result = SaleReturn.Create(
            shopId,
            saleId,
            returnNumber: "RET-20260504-ABC123EF",
            processedAt,
            processedBy,
            notes: "  Damaged packet  ",
            totalRefundAmount: 118m,
            dueReductionAmount: 50m,
            payoutAmount: 68m,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: 200m,
            customerBalanceAfter: 150m,
            items: []);

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
        Assert.Empty(saleReturn.Items);
    }

    [Fact]
    public void CreateLine_WithValidSnapshot_SetsReturnLineFields()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();

        var result = SaleReturnItem.Create(
            shopId,
            saleId,
            saleItemId,
            quantity: 2.5m,
            condition: SaleReturnCondition.Wastage,
            originalCostPrice: 80m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 18m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: 250m,
            approvedRefundAmount: 200m,
            taxableAmount: 250m,
            taxAmount: 45m,
            notes: "  Torn label  ");

        Assert.False(result.IsError);
        var line = result.Value;
        Assert.Equal(shopId, line.ShopId);
        Assert.Equal(saleId, line.SaleId);
        Assert.Equal(saleItemId, line.SaleItemId);
        Assert.Equal(2.5m, line.Quantity);
        Assert.Equal(SaleReturnCondition.Wastage, line.Condition);
        Assert.Equal(80m, line.OriginalCostPrice);
        Assert.Equal(100m, line.OriginalSalesPrice);
        Assert.Equal(18m, line.OriginalTaxRatePercent);
        Assert.False(line.OriginalIsPriceIncludingTax);
        Assert.Equal(250m, line.MaxRefundAmount);
        Assert.Equal(200m, line.ApprovedRefundAmount);
        Assert.Equal(250m, line.TaxableAmount);
        Assert.Equal(45m, line.TaxAmount);
        Assert.Equal("Torn label", line.Notes);
    }

    [Fact]
    public void Void_WithReason_MarksReturnVoided()
    {
        var processedAt = new DateTimeOffset(2026, 5, 4, 10, 15, 0, TimeSpan.Zero);
        var voidedAt = processedAt.AddHours(1);
        var voidedBy = Guid.NewGuid();
        var saleReturn = SaleReturn.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RET-20260504-ABC123EF",
            processedAt,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: 0m,
            dueReductionAmount: 0m,
            payoutAmount: 0m,
            totalTaxableAmount: 0m,
            totalTaxAmount: 0m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            items: []).Value;

        var result = saleReturn.Void(voidedAt, voidedBy, "  Entry mistake  ");

        Assert.False(result.IsError);
        Assert.True(saleReturn.IsVoided);
        Assert.Equal(voidedAt, saleReturn.VoidedAt);
        Assert.Equal(voidedBy, saleReturn.VoidedBy);
        Assert.Equal("Entry mistake", saleReturn.VoidReason);
    }
}
