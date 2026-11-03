using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using static Intelibill.Application.Unit.Tests.Features.Exports.Accounting.ReturnAccountingFixtures;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Accounting;

public class ReturnAdjustmentProjectorTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly DateOnly June1 = new(2026, 6, 1);
    private static readonly DateOnly June30 = new(2026, 6, 30);
    private static readonly DateOnly July1 = new(2026, 7, 1);
    private static readonly DateOnly July31 = new(2026, 7, 31);

    [Fact]
    public void Project_PartialRefund_CarriesSnapshotAndOriginalValueSeparately()
    {
        var sale = Sale(ShopId);
        var saleReturn = Return(sale, approved: 50m, taxable: 42.37m, tax: 7.63m);

        var entry = Assert.Single(Project([saleReturn], sale, June1, June30));

        Assert.Equal(ReturnAdjustmentEntryKind.Return, entry.Kind);
        Assert.Equal(ReturnAccountingStatus.Balanced, entry.Status);
        Assert.Equal("proportional-line-tax/1", entry.PolicyVersion);
        Assert.Equal(118m, entry.OriginalReturnedGross);
        Assert.Equal(50m, entry.FinancialAdjustment);
        Assert.Equal(42.37m, entry.TaxableAdjustment);
        Assert.Equal(7.63m, entry.TaxAdjustment);
        Assert.Equal(0m, entry.NonTaxAdjustment);
        var component = Assert.Single(entry.TaxComponents);
        Assert.Equal((18m, 42.37m, 7.63m), (component.TaxRatePercent, component.TaxableAmount, component.TaxAmount));
        Assert.Equal(50m, entry.SettledBy(ReturnSettlementKind.ExternalRefund));
        Assert.True(entry.HasVoucher);
        var line = Assert.Single(entry.Lines);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(SaleLineType.Goods, line.LineType);
        Assert.Equal(80m, line.OriginalCostPrice);
    }

    [Fact]
    public void Project_ReturnAndVoidInDifferentPeriods_EmitsEachInItsOwnRange()
    {
        var sale = Sale(ShopId);
        var saleReturn = Return(sale, approved: 118m, taxable: 100m, tax: 18m);
        saleReturn.Void(new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero), Guid.NewGuid(), "Entered twice");

        var june = Assert.Single(Project([saleReturn], sale, June1, June30));
        var july = Assert.Single(Project([saleReturn], sale, July1, July31));
        var both = Project([saleReturn], sale, June1, July31);

        Assert.Equal(ReturnAdjustmentEntryKind.Return, june.Kind);
        Assert.Equal(118m, june.FinancialAdjustment);

        Assert.Equal(ReturnAdjustmentEntryKind.VoidReversal, july.Kind);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero), july.PostedAt);
        Assert.Equal(saleReturn.ProcessedAt, july.ReturnProcessedAt);
        Assert.Equal(-118m, july.FinancialAdjustment);
        Assert.Equal(-100m, july.TaxableAdjustment);
        Assert.Equal(-18m, july.TaxAdjustment);
        Assert.Equal(-118m, july.OriginalReturnedGross);
        Assert.Equal(-1m, Assert.Single(july.Lines).Quantity);
        Assert.Equal(-118m, Assert.Single(july.Settlements).Amount);
        Assert.Equal("Entered twice", july.VoidReason);
        Assert.True(july.HasVoucher);

        Assert.Equal(2, both.Count);
        Assert.Equal(0m, both.Sum(e => e.FinancialAdjustment));
    }

    [Fact]
    public void Project_ReturnOutsideRange_NotIncludedEvenWhenSaleIs()
    {
        var sale = Sale(ShopId);
        var saleReturn = Return(sale, approved: 118m, taxable: 100m, tax: 18m, processedAt: new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(Project([saleReturn], sale, June1, June30));
        Assert.Single(Project([saleReturn], sale, July1, July31));
    }

    [Fact]
    public void Project_LegacyWithoutSnapshot_NeedsReview()
    {
        var sale = Sale(ShopId);
        var saleReturn = Return(sale, approved: 118m, taxable: 100m, tax: 18m, post: false);

        var entry = Assert.Single(Project([saleReturn], sale, June1, June30));

        Assert.Equal(ReturnAccountingStatus.NeedsReview, entry.Status);
        Assert.Equal(ReturnAdjustmentProjector.MissingSnapshotReason, entry.ReviewReason);
        Assert.Null(entry.TaxableAdjustment);
        Assert.Empty(entry.TaxComponents);
        Assert.Empty(entry.Settlements);
        Assert.False(entry.HasVoucher);
        Assert.Equal(118m, entry.FinancialAdjustment);
    }

    [Fact]
    public void Project_ZeroRefund_KeepsStockFactWithoutVoucher()
    {
        var sale = Sale(ShopId);
        var saleReturn = Return(sale, approved: 0m, taxable: 0m, tax: 0m);

        var entry = Assert.Single(Project([saleReturn], sale, June1, June30));

        Assert.Equal(ReturnAccountingStatus.Balanced, entry.Status);
        Assert.Equal(0m, entry.FinancialAdjustment);
        Assert.Equal(118m, entry.OriginalReturnedGross);
        Assert.Empty(entry.TaxComponents);
        Assert.False(entry.HasVoucher);
        Assert.Equal(1m, Assert.Single(entry.Lines).Quantity);
    }

    [Fact]
    public void Project_SettlementCarriesCreditNoteCode()
    {
        var customerId = Guid.NewGuid();
        var creditNoteId = Guid.NewGuid();
        var sale = Sale(ShopId, customerId: customerId);
        var saleReturn = Return(sale, approved: 100m, taxable: 84.75m, tax: 15.25m, dueReduction: 40m,
            destination: ReturnPayoutDestination.CreditNote, creditNoteId: creditNoteId);

        var entry = Assert.Single(ReturnAdjustmentProjector.Project(
            [saleReturn],
            new Dictionary<Guid, Sale> { [sale.Id] = sale },
            new Dictionary<Guid, string> { [creditNoteId] = "CN-0001" },
            June1,
            June30));

        Assert.Collection(
            entry.Settlements,
            receivable =>
            {
                Assert.Equal(ReturnSettlementKind.ReceivableReduction, receivable.Kind);
                Assert.Equal(40m, receivable.Amount);
                Assert.Equal(customerId, receivable.CustomerId);
            },
            storeCredit =>
            {
                Assert.Equal(ReturnSettlementKind.StoreCreditIssued, storeCredit.Kind);
                Assert.Equal(60m, storeCredit.Amount);
                Assert.Equal("CN-0001", storeCredit.CreditNoteCode);
            });
    }

    [Fact]
    public void Project_OrdersByPostingTime()
    {
        var sale = Sale(ShopId);
        var later = Return(sale, approved: 118m, taxable: 100m, tax: 18m, returnNumber: "RET-B", processedAt: new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));
        var earlier = Return(sale, approved: 118m, taxable: 100m, tax: 18m, returnNumber: "RET-A", processedAt: new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero));

        var entries = Project([later, earlier], sale, June1, June30);

        Assert.Equal(["RET-A", "RET-B"], entries.Select(e => e.ReturnNumber));
    }

    private static IReadOnlyList<ReturnAdjustmentEntry> Project(IEnumerable<SaleReturn> returns, Sale sale, DateOnly from, DateOnly to) =>
        ReturnAdjustmentProjector.Project(
            returns,
            new Dictionary<Guid, Sale> { [sale.Id] = sale },
            new Dictionary<Guid, string>(),
            from,
            to);
}
