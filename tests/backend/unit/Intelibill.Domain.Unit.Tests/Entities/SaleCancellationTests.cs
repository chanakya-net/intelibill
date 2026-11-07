using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SaleCancellationTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly Guid SaleId = Guid.NewGuid();
    private static readonly InvoiceSeller Seller = new("Seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered);
    private static readonly InvoiceBuyer Buyer = new("Buyer", null, null, null, null, null);

    private static SaleCancellationLineInput Line() => new(ShopId, SaleId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m, "Sugar", "KGS", 60m, 200m, 120m);

    private static SaleCancellationInput Input() => new(ShopId, SaleId, Guid.NewGuid(), " operation ", " mistaken entry ", DateTimeOffset.UtcNow,
        new DateOnly(2026, 11, 8), "Asia/Kolkata", "A/2627/000001", new DateOnly(2026, 11, 7),
        200m, 200m, 0m, 120m, 80m, Guid.NewGuid(), Seller, Buyer, true, true, [Line()]);

    private static ErrorOr.ErrorOr<SaleCancellation> Create(decimal total = 200m, decimal due = 120m, decimal cash = 80m, Guid? ledger = null, bool refund = true) =>
        SaleCancellation.Record(Input() with { TotalAmount = total, RevenueAmount = total, DueReductionAmount = due, CashRefundAmount = cash, CustomerLedgerEntryId = ledger, CashRefundConfirmed = refund });

    [Fact]
    public void Create_MixedSettlement_FreezesExactAmountsAndIdentities()
    {
        var ledger = Guid.NewGuid();
        var result = Create(ledger: ledger);
        Assert.False(result.IsError);
        Assert.Equal(200m, result.Value.TotalAmount);
        Assert.Equal(120m, result.Value.DueReductionAmount);
        Assert.Equal(80m, result.Value.CashRefundAmount);
        Assert.Equal(ledger, result.Value.CustomerLedgerEntryId);
        Assert.Equal("operation", result.Value.OperationKey);
        Assert.Equal("mistaken entry", result.Value.Reason);
        Assert.Equal(Seller, result.Value.Seller);
        Assert.Equal(Buyer, result.Value.Buyer);
        Assert.Matches("^SC-[a-f0-9]{32}$", result.Value.ReferenceNumber);
    }

    [Theory]
    [InlineData(200, 120, 79, true, true)]
    [InlineData(200, 120, 80, false, true)]
    [InlineData(200, 120, 80, true, false)]
    [InlineData(200, 200, 0, true, true)]
    [InlineData(200, 0, 200, true, true)]
    public void Create_InconsistentSettlement_IsRejected(decimal total, decimal due, decimal cash, bool ledger, bool refund)
    {
        Assert.True(Create(total, due, cash, ledger ? Guid.NewGuid() : null, refund).IsError);
    }

    [Fact]
    public void Create_ZeroDue_HasNoLedgerLink()
    {
        var result = Create(due: 0m, cash: 200m);
        Assert.False(result.IsError);
        Assert.Null(result.Value.CustomerLedgerEntryId);
    }

    [Fact]
    public void Create_ZeroCash_RecordsNoRefundConfirmation()
    {
        var result = Create(due: 200m, cash: 0m, ledger: Guid.NewGuid(), refund: false);
        Assert.False(result.IsError);
        Assert.False(result.Value.CashRefundConfirmed);
    }

    [Fact]
    public void Record_EmptyLines_IsRejected() => Assert.True(SaleCancellation.Record(Input() with { Lines = [] }).IsError);

    [Fact]
    public void Record_IncompleteRevenue_IsRejected() =>
        Assert.True(SaleCancellation.Record(Input() with { Lines = [Line() with { RevenueAmount = 100m }] }).IsError);

    [Fact]
    public void Record_DuplicateOriginalLine_IsRejected()
    {
        var line = Line() with { RevenueAmount = 100m };
        Assert.True(SaleCancellation.Record(Input() with { Lines = [line, line with { StockTransactionId = Guid.NewGuid() }] }).IsError);
    }

    [Fact]
    public void Record_DuplicateStockEffect_IsRejected()
    {
        var line = Line() with { RevenueAmount = 100m };
        Assert.True(SaleCancellation.Record(Input() with { Lines = [line, line with { SaleItemId = Guid.NewGuid() }] }).IsError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Record_ForeignLine_IsRejected(bool foreignShop)
    {
        var line = foreignShop ? Line() with { ShopId = Guid.NewGuid() } : Line() with { SaleId = Guid.NewGuid() };
        Assert.True(SaleCancellation.Record(Input() with { Lines = [line] }).IsError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Record_NonpositiveQuantity_IsRejected(decimal quantity) =>
        Assert.True(SaleCancellation.Record(Input() with { Lines = [Line() with { Quantity = quantity }] }).IsError);

    [Fact]
    public void Record_CopiesAllFactsBeforeReturningSuccess()
    {
        List<SaleCancellationLineInput> lines = [Line()];
        var input = Input() with { Lines = lines };
        var cancellation = SaleCancellation.Record(input).Value;
        var before = JsonSerializer.Serialize(cancellation);
        lines.Clear();
        lines.Add(Line() with { Description = "Replacement", RevenueAmount = 1m });
        Assert.Equal(before, JsonSerializer.Serialize(cancellation));
        var recordedLine = Assert.Single(cancellation.Lines);
        Assert.Equal(cancellation.Id, recordedLine.SaleCancellationId);
        Assert.Equal(ShopId, recordedLine.ShopId);
        Assert.Equal(SaleId, recordedLine.SaleId);
        Assert.Equal("Sugar", recordedLine.Description);
        Assert.Equal(200m, recordedLine.RevenueAmount);
        Assert.Throws<NotSupportedException>(() => ((ICollection<SaleCancellationLine>)cancellation.Lines).Clear());
    }

    [Fact]
    public void MarkCancelled_PreservesOriginalIssueFactsAndIsTerminal()
    {
        var sale = Sale.Create(ShopId, "A/2627/000001", null, "Buyer", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 80m, 120m, 200m, 0m, []);
        sale.AttachInvoiceDocument(new IssuedInvoiceSnapshot(2, InvoiceDocumentType.Receipt, sale.SoldAt, sale.BusinessDate, 2026, "A", sale.InvoiceNumber, Seller, Buyer,
            "29", "Karnataka", false, false, [], new(0m, 200m, [], 0m, 0m, 200m), [], InvoiceSignatureMode.AuthorisedSignatory, "component-split/1"));
        var before = JsonSerializer.Serialize(sale);
        Assert.Equal(SaleStatus.Posted, sale.Status);
        Assert.False(sale.MarkCancelled().IsError);
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
        Assert.Equal(before.Replace("\"Status\":0", "\"Status\":1", StringComparison.Ordinal), JsonSerializer.Serialize(sale));
        Assert.Equal("SaleCancellation.AlreadyCancelled", sale.MarkCancelled().FirstError.Code);
    }
}
