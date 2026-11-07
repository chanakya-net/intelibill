using System.Linq.Expressions;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Features.Sales.Services.Cancellations;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Cancellations;

public sealed class SaleCancellationPolicyTests
{
    private readonly ISaleReturnRepository returns = Substitute.For<ISaleReturnRepository>();
    private readonly ICreditNoteRedemptionRepository redemptions = Substitute.For<ICreditNoteRedemptionRepository>();
    private readonly IPostingPeriodRepository periods = Substitute.For<IPostingPeriodRepository>();
    private readonly IInventoryBatchRepository batches = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository inventory = Substitute.For<IInventoryRepository>();
    private readonly IPaymentReceiptRepository payments = Substitute.For<IPaymentReceiptRepository>();
    private readonly IInvoiceBalanceReader balances = Substitute.For<IInvoiceBalanceReader>();
    private readonly ICustomerLedgerEntryRepository ledger = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly DateTimeOffset now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
    private SaleCancellationPolicy Policy => new(returns, redemptions, periods, batches, inventory, payments, balances, ledger);
    private Sale Fixture(bool duplicate = false, bool service = false)
    {
        var shop = Guid.NewGuid(); var actor = Guid.NewGuid(); var item = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop, item, "LOT", 8, 60, 100, 100, 0, false, null, null, null, actor).Value;
        var stock = Intelibill.Domain.Entities.Inventory.Create(shop, item, 8, 0, 100, actor).Value;
        batches.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        inventory.GetByItemAsync(shop, item, Arg.Any<CancellationToken>()).Returns(stock);
        returns.GetBySaleAsync(shop, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        redemptions.FindAsync(Arg.Any<Expression<Func<CreditNoteRedemption, bool>>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<CreditNoteRedemption>());
        periods.GetRangeAsync(shop, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<PostingPeriod>());
        SaleLineInput Line(decimal q, string name) => new(shop, service ? SaleLineType.Service : SaleLineType.Goods, service ? null : item, service ? null : batch.Id, service ? Guid.NewGuid() : null, name, "SUGAR", q, 60, 100, 100, 0, false, false);
        var sale = Sale.Record(shop, actor, "KEY", "HASH", "A/2627/000001", duplicate ? [Line(1, "Sugar"), Line(1, "Sugar")] : [Line(1, "Sugar"), Line(2, "Salt")], null, null, null, PaymentMethod.Cash, duplicate ? 200m : 300m, 0, now, BusinessDay.InDefaultZone(now)).Value;
        sale.AttachInvoiceDocument(new(2, InvoiceDocumentType.Receipt, now, sale.BusinessDate, 2026, "A", sale.InvoiceNumber, new("Seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered), new(null, null, null, null, null, null), "29", "Karnataka", false, false,
            sale.Items.Select((x, i) => new InvoiceDocumentLine(i + 1, x.LineName, null, "KGS", x.Quantity, 100, 0, x.TotalAmount, 0, false, [], x.TotalAmount)).ToArray(), new(sale.TotalAmount, 0, [], 0, 0, sale.TotalAmount), [], InvoiceSignatureMode.AuthorisedSignatory, "component-split/1"));
        return sale;
    }
    [Fact]
    public async Task ReversedPersistedCollectionOrder_MatchesFrozenLineFacts()
    {
        var s = Fixture();
        var list = (List<SaleItem>)typeof(Sale).GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(s)!;
        list.Reverse();
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.True(e.Eligible);
        Assert.Equal("Salt", e.Lines.Single(x => x.SaleItemId == s.Items[0].Id).Description);
    }
    [Fact]
    public async Task IdenticalDuplicateLines_AreInterchangeable()
    {
        var s = Fixture(true);
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.True(e.Eligible);
        Assert.Equal(2, e.Lines.Count);
    }
    [Fact]
    public async Task AmbiguousDifferingUqc_RefusesAttribution()
    {
        var s = Fixture(true);
        var document = s.InvoiceDocument!;
        typeof(Sale).GetProperty(nameof(Sale.InvoiceDocument))!.SetValue(s, document with { Lines = [document.Lines[0], document.Lines[1] with { Uom = "OTH" }] });
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.False(e.Eligible);
        Assert.Contains(e.Reasons, x => x.Code == "SaleCancellation.SnapshotMismatch");
    }
    [Fact]
    public async Task MonthBoundary_UsesShopLocalBusinessDay()
    {
        var s = Fixture();
        var e = await Policy.EvaluateAsync(s.ShopId, s, new(new DateOnly(2026, 12, 1), "Asia/Kolkata"), CancellationToken.None);
        Assert.Contains(e.Reasons, x => x.Code == "SaleCancellation.OtherMonthBlocked");
    }
    [Fact]
    public async Task ServiceBlocked_EvenWithReceiptAndCash()
    {
        var s = Fixture(service: true);
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.Contains(e.Reasons, x => x.Code == "SaleCancellation.ServiceBlocked");
    }
    [Fact]
    public async Task PersistedRedemption_BlocksEvenWhenHeaderAppliedAmountIsZero()
    {
        var s = Fixture();
        var note = CreditNote.Issue(s.ShopId, Guid.NewGuid(), 10m, "Original credit", "CREDIT", null).Value;
        var redemption = note.Redeem(s.ShopId, s.Id, 10m).Value;
        redemptions.FindAsync(Arg.Any<Expression<Func<CreditNoteRedemption, bool>>>(), Arg.Any<CancellationToken>()).Returns(new[] { redemption });
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.Contains(e.Reasons, x => x.Code == "SaleCancellation.CreditRestorationRequired");
        Assert.Equal(10m, e.CreditRestorationAmount);
    }
    [Theory]
    [InlineData("rounding")]
    [InlineData("header-tax")]
    [InlineData("line-tax")]
    [InlineData("component-tax")]
    [InlineData("line-discount")]
    [InlineData("wrong-price")]
    [InlineData("wrong-hsn")]
    [InlineData("duplicate-ordinal")]
    public async Task InconsistentFrozenMoneyOrLineFacts_BlockCancellation(string variant)
    {
        var s = Fixture(); var d = s.InvoiceDocument!;
        var first = d.Lines[0];
        var changed = variant switch
        {
            "rounding" => d with { Totals = d.Totals with { RoundingAdjustment = 1m } },
            "header-tax" => d with { Totals = d.Totals with { TotalTax = 1m } },
            "line-tax" => d with { Lines = [first with { TaxRatePercent = 1m }, d.Lines[1]] },
            "component-tax" => d with { Lines = [first with { TaxComponents = [new(TaxComponentKind.Igst, 1m, 100m, 1m)] }, d.Lines[1]] },
            "line-discount" => d with { Lines = [first with { DiscountAmount = 1m }, d.Lines[1]] },
            "wrong-price" => d with { Lines = [first with { UnitPrice = 99m }, d.Lines[1]] },
            "wrong-hsn" => d with { Lines = [first with { HsnOrSac = "OTHER" }, d.Lines[1]] },
            _ => d with { Lines = [first, d.Lines[1] with { LineNumber = 1 }] }
        };
        typeof(Sale).GetProperty(nameof(Sale.InvoiceDocument))!.SetValue(s, changed);
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.Contains(e.Reasons, x => x.Code == "SaleCancellation.SnapshotMismatch");
    }

    [Theory]
    [InlineData("seller")]
    [InlineData("buyer")]
    [InlineData("totals")]
    [InlineData("lines")]
    [InlineData("line")]
    [InlineData("header-components")]
    [InlineData("line-components")]
    public async Task MalformedStoredSnapshot_IsIneligibleInsteadOfThrowing(string missing)
    {
        var s = Fixture(); var d = s.InvoiceDocument!;
        var malformed = missing switch
        {
            "seller" => d with { Seller = null! },
            "buyer" => d with { Buyer = null! },
            "totals" => d with { Totals = null! },
            "lines" => d with { Lines = null! },
            "line" => d with { Lines = [null!, d.Lines[1]] },
            "header-components" => d with { Totals = d.Totals with { Components = null! } },
            _ => d with { Lines = [d.Lines[0] with { TaxComponents = null! }, d.Lines[1]] }
        };
        typeof(Sale).GetProperty(nameof(Sale.InvoiceDocument))!.SetValue(s, malformed);
        var e = await Policy.EvaluateAsync(s.ShopId, s, BusinessDay.InDefaultZone(now), CancellationToken.None);
        Assert.Contains(e.Reasons, x => x.Code == "SaleCancellation.SnapshotMismatch");
    }

}
