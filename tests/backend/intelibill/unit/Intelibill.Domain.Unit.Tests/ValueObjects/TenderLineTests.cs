using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public sealed class TenderLineTests
{
    [Fact]
    public void CashTenderedLessChange_IsAppliedCash()
    {
        var line = new TenderLine(ReceiptTenderMethod.Cash, 300m, 500m, 200m, null, null);
        Assert.False(line.Validate().IsError);
        Assert.False(TenderLine.ValidateLines([line], 300m).IsError);
    }

    [Theory]
    [InlineData(2, -1d, null, null)]
    [InlineData(2, 1.001, null, null)]
    [InlineData(2, 300d, 500d, null)]
    [InlineData(2, 300d, null, 200d)]
    [InlineData(2, 300d, 500d, 199d)]
    [InlineData(2, 300d, 200d, -100d)]
    [InlineData(3, 300d, 500d, 200d)]
    [InlineData(1, 300d, null, null)]
    [InlineData(99, 300d, null, null)]
    public void InvalidTender_IsRejected(int method, double applied, double? tendered, double? change)
    {
        var line = new TenderLine((ReceiptTenderMethod)method, (decimal)applied,
            tendered.HasValue ? (decimal)tendered.Value : null, change.HasValue ? (decimal)change.Value : null, null, null);
        Assert.True(line.Validate().IsError);
    }

    [Fact]
    public void MixedMissingAndMismatchedLines_AreRejected()
    {
        var cash = new TenderLine(ReceiptTenderMethod.Cash, 100m, null, null, null, null);
        Assert.True(TenderLine.ValidateLines([cash, cash], 200m).IsError);
        Assert.True(TenderLine.ValidateLines([], 100m).IsError);
        Assert.True(TenderLine.ValidateLines([cash], 90m).IsError);
        Assert.True(TenderLine.ValidateLines([cash], 0m).IsError);
        Assert.False(TenderLine.ValidateLines([], 0m).IsError);
        Assert.Null(cash.CashTendered);
    }

    [Theory]
    [InlineData(TillSettlementDisposition.NoPhysicalMovement, false, true)]
    [InlineData(TillSettlementDisposition.NoPhysicalMovement, true, false)]
    [InlineData(TillSettlementDisposition.MoneyReceived, true, true)]
    [InlineData(TillSettlementDisposition.MoneyPaid, false, false)]
    public void Evidence_RequiresConcreteTenderOnlyForPhysicalMoney(TillSettlementDisposition disposition, bool hasTender, bool valid)
    {
        var evidence = new TillSettlementEvidence(disposition,
            hasTender ? new TenderLine(ReceiptTenderMethod.Cash, 100m, null, null, null, null) : null, "explained");
        Assert.Equal(valid, !evidence.Validate().IsError);
    }
}
