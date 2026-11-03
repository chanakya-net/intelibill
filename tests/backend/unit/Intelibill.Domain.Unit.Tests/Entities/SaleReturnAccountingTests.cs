using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleReturnAccountingTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly Guid SaleItemId = Guid.NewGuid();

    [Fact]
    public void PostAccounting_BalancedSnapshot_SetsStatusAndLineSplit()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 50m, ReturnPayoutDestination.Refund);
        var snapshot = Snapshot(
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 50m)]);

        var result = saleReturn.PostAccounting(snapshot);

        Assert.False(result.IsError);
        Assert.Equal(ReturnAccountingStatus.Balanced, saleReturn.AccountingStatus);
        Assert.Equal("test-policy/1", saleReturn.AccountingPolicyVersion);
        Assert.Equal(42.37m, saleReturn.AdjustmentTaxableAmount);
        Assert.Equal(7.63m, saleReturn.AdjustmentTaxAmount);
        Assert.Equal(0m, saleReturn.AdjustmentNonTaxAmount);
        Assert.Equal(118m, saleReturn.OriginalReturnedGross);

        var item = Assert.Single(saleReturn.Items);
        Assert.Equal(118m, item.MaxRefundAmount);
        Assert.Equal(100m, item.TaxableAmount);
        Assert.Equal(18m, item.TaxAmount);
        Assert.Equal(42.37m, item.AdjustmentTaxableAmount);
        Assert.Equal(7.63m, item.AdjustmentTaxAmount);

        var settlement = Assert.Single(saleReturn.Settlements);
        Assert.Equal(ReturnSettlementKind.ExternalRefund, settlement.Kind);
        Assert.Equal(50m, settlement.Amount);
        Assert.Equal(1, settlement.Sequence);
        Assert.Equal(saleReturn.Id, settlement.SaleReturnId);
        Assert.Equal(ShopId, settlement.ShopId);
    }

    [Fact]
    public void PostAccounting_MixedSettlement_KeepsEachLegAndReference()
    {
        var customerId = Guid.NewGuid();
        var ledgerEntryId = Guid.NewGuid();
        var creditNoteId = Guid.NewGuid();
        var saleReturn = PartialRefund(dueReduction: 20m, payout: 30m, ReturnPayoutDestination.CreditNote);

        var result = saleReturn.PostAccounting(Snapshot(
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [
                new ReturnSettlementInput(ReturnSettlementKind.ReceivableReduction, 20m, customerId, CustomerLedgerEntryId: ledgerEntryId),
                new ReturnSettlementInput(ReturnSettlementKind.StoreCreditIssued, 30m, customerId, creditNoteId),
            ]));

        Assert.False(result.IsError);
        Assert.Collection(
            saleReturn.Settlements,
            receivable =>
            {
                Assert.Equal(ReturnSettlementKind.ReceivableReduction, receivable.Kind);
                Assert.Equal(20m, receivable.Amount);
                Assert.Equal(customerId, receivable.CustomerId);
                Assert.Equal(ledgerEntryId, receivable.CustomerLedgerEntryId);
                Assert.Null(receivable.CreditNoteId);
            },
            storeCredit =>
            {
                Assert.Equal(ReturnSettlementKind.StoreCreditIssued, storeCredit.Kind);
                Assert.Equal(30m, storeCredit.Amount);
                Assert.Equal(creditNoteId, storeCredit.CreditNoteId);
                Assert.Equal(2, storeCredit.Sequence);
            });
    }

    [Fact]
    public void PostAccounting_ZeroRefund_PostsWithoutSettlement()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 0m, payoutDestination: null, approved: 0m);

        var result = saleReturn.PostAccounting(Snapshot([new ReturnLineAccounting(SaleItemId, 0m, 0m, 0m)], []));

        Assert.False(result.IsError);
        Assert.Equal(ReturnAccountingStatus.Balanced, saleReturn.AccountingStatus);
        Assert.Empty(saleReturn.Settlements);
        Assert.Equal(118m, saleReturn.OriginalReturnedGross);
    }

    [Fact]
    public void PostAccounting_SettlementSumMismatch_Refused()
    {
        var result = ReturnAccountingSnapshot.Create(
            "test-policy/1",
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 49.99m)]);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.AccountingSettlementMismatch", result.FirstError.Code);
    }

    [Fact]
    public void PostAccounting_LineDoesNotMatchApprovedRefund_Refused()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 50m, ReturnPayoutDestination.Refund);

        var result = saleReturn.PostAccounting(Snapshot(
            [new ReturnLineAccounting(SaleItemId, 100m, 18m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 118m)]));

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.AccountingLineMismatch", result.FirstError.Code);
        Assert.Null(saleReturn.AccountingStatus);
        Assert.Null(Assert.Single(saleReturn.Items).AdjustmentTaxableAmount);
    }

    [Fact]
    public void PostAccounting_UnknownLine_Refused()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 50m, ReturnPayoutDestination.Refund);

        var result = saleReturn.PostAccounting(Snapshot(
            [new ReturnLineAccounting(Guid.NewGuid(), 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 50m)]));

        Assert.Equal("SaleReturn.AccountingLineMismatch", result.FirstError.Code);
    }

    [Fact]
    public void PostAccounting_ReceivableLegMustEqualDueReduction()
    {
        var saleReturn = PartialRefund(dueReduction: 20m, payout: 30m, ReturnPayoutDestination.Refund);

        var result = saleReturn.PostAccounting(Snapshot(
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [
                new ReturnSettlementInput(ReturnSettlementKind.ReceivableReduction, 30m),
                new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 20m),
            ]));

        Assert.Equal("SaleReturn.AccountingSettlementMismatch", result.FirstError.Code);
        Assert.Empty(saleReturn.Settlements);
    }

    [Fact]
    public void PostAccounting_StoreCreditRequiresCreditNoteDestination()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 50m, ReturnPayoutDestination.Refund);

        var result = saleReturn.PostAccounting(Snapshot(
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.StoreCreditIssued, 50m, CreditNoteId: Guid.NewGuid())]));

        Assert.Equal("SaleReturn.AccountingSettlementInvalid", result.FirstError.Code);
    }

    [Fact]
    public void Snapshot_StoreCreditWithoutCreditNote_Refused()
    {
        var result = ReturnAccountingSnapshot.Create(
            "test-policy/1",
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.StoreCreditIssued, 50m)]);

        Assert.Equal("SaleReturn.AccountingSettlementInvalid", result.FirstError.Code);
    }

    [Fact]
    public void PostAccounting_Twice_Refused()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 50m, ReturnPayoutDestination.Refund);
        var snapshot = Snapshot(
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 50m)]);
        Assert.False(saleReturn.PostAccounting(snapshot).IsError);

        var second = saleReturn.PostAccounting(snapshot);

        Assert.Equal("SaleReturn.AccountingAlreadyPosted", second.FirstError.Code);
        Assert.Single(saleReturn.Settlements);
    }

    [Fact]
    public void PostAccounting_Voided_Refused()
    {
        var saleReturn = PartialRefund(dueReduction: 0m, payout: 50m, ReturnPayoutDestination.Refund);
        Assert.False(saleReturn.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Mistake").IsError);

        var result = saleReturn.PostAccounting(Snapshot(
            [new ReturnLineAccounting(SaleItemId, 42.37m, 7.63m, 0m)],
            [new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, 50m)]));

        Assert.Equal("SaleReturn.AccountingVoided", result.FirstError.Code);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.005)]
    public void Snapshot_InvalidLineAmount_Refused(double taxAmount)
    {
        var result = ReturnAccountingSnapshot.Create(
            "test-policy/1",
            [new ReturnLineAccounting(SaleItemId, 50m, (decimal)taxAmount, 0m)],
            []);

        Assert.Equal("SaleReturn.AccountingAmountInvalid", result.FirstError.Code);
    }

    [Fact]
    public void Snapshot_PolicyVersionRequired()
    {
        var result = ReturnAccountingSnapshot.Create(" ", [new ReturnLineAccounting(SaleItemId, 0m, 0m, 0m)], []);

        Assert.Equal("SaleReturn.AccountingPolicyRequired", result.FirstError.Code);
    }

    private static ReturnAccountingSnapshot Snapshot(
        IReadOnlyList<ReturnLineAccounting> lines,
        IReadOnlyList<ReturnSettlementInput> settlements)
    {
        var result = ReturnAccountingSnapshot.Create("test-policy/1", lines, settlements);
        Assert.False(result.IsError);
        return result.Value;
    }

    private static SaleReturn PartialRefund(
        decimal dueReduction,
        decimal payout,
        ReturnPayoutDestination? payoutDestination,
        decimal approved = 50m)
    {
        var line = new SaleReturnLineInput(
            ShopId,
            SaleItemId,
            1m,
            SaleReturnCondition.Restockable,
            80m,
            100m,
            18m,
            false,
            MaxRefundAmount: 118m,
            ApprovedRefundAmount: approved,
            TaxableAmount: 100m,
            TaxAmount: 18m,
            Notes: "Partial refund");

        var result = SaleReturn.Record(
            ShopId,
            Guid.NewGuid(),
            "RET-20261103-ACC00001",
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: approved,
            dueReductionAmount: dueReduction,
            payoutAmount: payout,
            payoutDestination: payoutDestination,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [line]);

        Assert.False(result.IsError);
        return result.Value;
    }
}
