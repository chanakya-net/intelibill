using Intelibill.Application.Features.Sales.Services.Returns;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class ProportionalLineTaxReturnAccountingPolicyTests
{
    private readonly ProportionalLineTaxReturnAccountingPolicy _policy = new();
    private static readonly Guid SaleItemId = Guid.NewGuid();

    [Fact]
    public void Version_IsDeclared()
    {
        Assert.Equal("proportional-line-tax/1", _policy.Version);
    }

    [Fact]
    public void PartialRefund_SplitsProportionally()
    {
        var line = Assert.Single(Classify(shareGross: 118m, shareTax: 18m, approved: 50m));

        Assert.Equal(42.37m, line.TaxableAmount);
        Assert.Equal(7.63m, line.TaxAmount);
        Assert.Equal(0m, line.NonTaxAmount);
        Assert.Equal(50m, line.Total);
    }

    [Fact]
    public void FullRefund_ReturnsShareTaxExactly()
    {
        var line = Assert.Single(Classify(shareGross: 33.34m, shareTax: 5.09m, approved: 33.34m));

        Assert.Equal(28.25m, line.TaxableAmount);
        Assert.Equal(5.09m, line.TaxAmount);
        Assert.Equal(0m, line.NonTaxAmount);
    }

    [Fact]
    public void ZeroRefund_AllZero()
    {
        var line = Assert.Single(Classify(shareGross: 118m, shareTax: 18m, approved: 0m));

        Assert.Equal(0m, line.Total);
        Assert.Equal(0m, line.TaxAmount);
    }

    [Fact]
    public void ZeroRefundOfZeroValueShare_IsSupported()
    {
        var line = Assert.Single(Classify(shareGross: 0m, shareTax: 0m, approved: 0m));

        Assert.Equal(0m, line.Total);
    }

    [Fact]
    public void OverRefund_ExcessIsNonTaxAdjustment()
    {
        var line = Assert.Single(Classify(shareGross: 118m, shareTax: 18m, approved: 130m));

        Assert.Equal(100m, line.TaxableAmount);
        Assert.Equal(18m, line.TaxAmount);
        Assert.Equal(12m, line.NonTaxAmount);
    }

    [Fact]
    public void UntaxedShare_ReversesNoTax()
    {
        var line = Assert.Single(Classify(shareGross: 40m, shareTax: 0m, approved: 25m));

        Assert.Equal(25m, line.TaxableAmount);
        Assert.Equal(0m, line.TaxAmount);
    }

    [Fact]
    public void ZeroValueShareWithRefund_Unsupported()
    {
        var result = _policy.Classify([new ReturnAccountingLineRequest(SaleItemId, 0m, 0m, 10m)]);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.AccountingPolicyUnsupported", result.FirstError.Code);
        Assert.Contains(SaleItemId.ToString(), result.FirstError.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TaxAboveShare_Unsupported()
    {
        var result = _policy.Classify([new ReturnAccountingLineRequest(SaleItemId, 10m, 11m, 5m)]);

        Assert.Equal("SaleReturn.AccountingPolicyUnsupported", result.FirstError.Code);
    }

    [Fact]
    public void EveryLine_TotalsItsApprovedRefund()
    {
        var shares = new[] { (11.81m, 1.80m, 7.77m), (0.01m, 0m, 0.01m), (99.99m, 15.25m, 33.33m), (236m, 36m, 1m) };

        foreach (var (gross, tax, approved) in shares)
        {
            var line = Assert.Single(Classify(gross, tax, approved));
            Assert.Equal(approved, line.Total);
            Assert.InRange(line.TaxAmount, 0m, tax);
        }
    }

    private IReadOnlyList<Domain.ValueObjects.ReturnLineAccounting> Classify(decimal shareGross, decimal shareTax, decimal approved)
    {
        var result = _policy.Classify([new ReturnAccountingLineRequest(SaleItemId, shareGross, shareTax, approved)]);
        Assert.False(result.IsError);
        return result.Value;
    }
}
