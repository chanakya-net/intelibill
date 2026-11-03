using System.Globalization;
using System.Xml.Linq;
using ErrorOr;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class SalesTallyXmlExportRendererTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReturnedAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly SalesTallyXmlExportRenderer _renderer = new(new AccountingVoucherValidator());

    // ── Envelope and ledgers ───────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_WithValidDataset_ReturnsVersionedEnvelope()
    {
        var result = await RenderAsync(Dataset([Sale("INV-001", "Alice", "Cash", paid: 590m, due: 0m, (18m, 500m, 90m))]));

        Assert.Equal("application/xml", result.ContentType);
        var root = Parse(result);
        Assert.Equal("ENVELOPE", root.Name.LocalName);
        Assert.Equal("Intelibill Sales Export (accounting v2)", root.Descendants("REQUESTDESC").Single().Value);
    }

    [Fact]
    public async Task RenderAsync_WithEmptyDataset_ReturnsValidEnvelopeWithNoVouchers()
    {
        var root = Parse(await RenderAsync(Dataset([])));

        Assert.Empty(root.Descendants("VOUCHER"));
        Assert.NotEmpty(root.Descendants("MASTER"));
    }

    [Fact]
    public async Task RenderAsync_CreatesLedgerMasterForEveryLedgerUsed()
    {
        var sale = Sale("INV-001", "Customer A", "Cash", paid: 1062m, due: 0m, (18m, 900m, 162m));
        var root = Parse(await RenderAsync(Dataset(
            [sale],
            [
                Return("RET-1", sale, financial: 130m, taxable: 100m, tax: 18m, nonTax: 12m,
                    Settle(ReturnSettlementKind.ExternalRefund, 130m)),
            ])));

        var groups = root.Descendants("MASTER")
            .ToDictionary(m => m.Element("NAME")!.Value, m => m.Element("GROUP")!.Value);
        var usedLedgers = root.Descendants("VOUCHERLINE").Select(l => l.Element("LEDGER")!.Value).Distinct();

        Assert.All(usedLedgers, ledger => Assert.True(groups.ContainsKey(ledger), $"No master for {ledger}"));
        Assert.Equal("Sales Accounts", groups["Sales"]);
        Assert.Equal("Bank Accounts", groups["Cash"]);
        Assert.Equal("Sundry Debtors", groups["Customer A"]);
        Assert.Equal("Tax Payable", groups["Output GST 18%"]);
        Assert.Equal("Current Liabilities", groups["Credit Note Liability"]);
        Assert.Equal("Current Liabilities", groups["Customer Refunds Clearing"]);
        Assert.Equal("Indirect Expenses", groups["Sales Return Adjustments"]);
    }

    // ── Sales vouchers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_WithNormalSale_UsesTaxableSalesCreditAndDebitPayment()
    {
        var voucher = SingleVoucher(Parse(await RenderAsync(Dataset([Sale("INV-001", "Alice", "Cash", paid: 590m, due: 0m, (18m, 500m, 90m))]))), "Sales");

        Assert.Equal("INV-001", voucher.Element("VOUCHERNUMBER")!.Value);
        Assert.Equal("20260501", voucher.Element("DATE")!.Value);
        Assert.Equal(
            [("Sales", 500m, false), ("Cash", 590m, true), ("Output GST 18%", 90m, false)],
            Lines(voucher));
    }

    [Fact]
    public async Task RenderAsync_GroupsGstByTaxRatePerInvoice()
    {
        var root = Parse(await RenderAsync(Dataset(
        [
            Sale("INV-001", "Alice", "Cash", paid: 223m, due: 0m, (18m, 100m, 18m), (5m, 100m, 5m)),
            Sale("INV-002", "Bob", "UPI", paid: 236m, due: 0m, (18m, 150m, 27m), (18m, 50m, 9m)),
        ])));

        var vouchers = root.Descendants("VOUCHER").ToDictionary(v => v.Element("VOUCHERNUMBER")!.Value);
        Assert.Equal(
            [("Output GST 5%", 5m, false), ("Output GST 18%", 18m, false)],
            Lines(vouchers["INV-001"]).Where(l => l.Ledger.StartsWith("Output GST", StringComparison.Ordinal)));
        Assert.Equal(
            [("Output GST 18%", 36m, false)],
            Lines(vouchers["INV-002"]).Where(l => l.Ledger.StartsWith("Output GST", StringComparison.Ordinal)));
        AssertEveryVoucherBalances(root);
    }

    [Theory]
    [InlineData("Alice", "Alice")]
    [InlineData(null, "Walk-in Customer")]
    public async Task RenderAsync_WithDueAmount_DebitsCustomerLedger(string? customerName, string expectedLedger)
    {
        var voucher = SingleVoucher(Parse(await RenderAsync(Dataset([Sale("INV-001", customerName, "Cash", paid: 90m, due: 500m, (18m, 500m, 90m))]))), "Sales");

        Assert.Contains((expectedLedger, 500m, true), Lines(voucher));
        Assert.Contains(("Cash", 90m, true), Lines(voucher));
    }

    [Theory]
    [InlineData("Cash", "Cash")]
    [InlineData("UPI", "UPI")]
    [InlineData("Card", "Card")]
    [InlineData("Credit", "Customer Receivable")]
    public async Task RenderAsync_WithPaymentMethods_CreatesCorrectPaymentLedgers(string method, string expectedLedger)
    {
        var voucher = SingleVoucher(Parse(await RenderAsync(Dataset([Sale("INV-001", "Alice", method, paid: 118m, due: 0m, (18m, 100m, 18m))]))), "Sales");

        Assert.Contains((expectedLedger, 118m, true), Lines(voucher));
    }

    [Fact]
    public async Task RenderAsync_WithCreditNoteApplied_DebitsLiabilityNotTender()
    {
        var (summary, lines) = Sale("INV-001", "Alice", "Cash", paid: 250m, due: 0m, (18m, 254.24m, 45.76m));
        var sale = (summary with { CreditNoteAppliedAmount = 50m }, lines);

        var voucher = SingleVoucher(Parse(await RenderAsync(Dataset([sale]))), "Sales");

        Assert.Contains(("Credit Note Liability", 50m, true), Lines(voucher));
        Assert.Contains(("Cash", 250m, true), Lines(voucher));
        Assert.DoesNotContain(Lines(voucher), l => l.Ledger is "UPI" or "Card");
    }

    [Fact]
    public async Task RenderAsync_SaleThatDoesNotBalance_IsRefusedNotPlugged()
    {
        // Paid ₹1100 against ₹590 of value: the old exporter wrote this as-is.
        var result = await _renderer.RenderAsync(Dataset([Sale("INV-009", "Alice", "Cash", paid: 1000m, due: 100m, (18m, 500m, 90m))]), CancellationToken.None);

        Assert.True(result.IsError);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("Export.VoucherUnbalanced", error.Code);
        Assert.Contains("INV-009", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_SummaryOnlyMultipleRatesAcrossInvoices_RefusedRatherThanGuessed()
    {
        var first = Sale("INV-001", "Alice", "Cash", paid: 118m, due: 0m, (18m, 100m, 18m));
        var second = Sale("INV-002", "Bob", "UPI", paid: 105m, due: 0m, (5m, 100m, 5m));
        var dataset = Dataset([first, second]) with { LineItemRows = [] };

        var result = await _renderer.RenderAsync(dataset, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.All(result.Errors, e => Assert.Equal("Export.VoucherUnbalanced", e.Code));
    }

    [Fact]
    public async Task RenderAsync_WithZeroValueSale_WritesNoVoucher()
    {
        var root = Parse(await RenderAsync(Dataset([Sale("INV-000", "Alice", "Cash", paid: 0m, due: 0m, (0m, 0m, 0m))])));

        Assert.Empty(root.Descendants("VOUCHER"));
    }

    // ── Return vouchers ────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_PartialRefund_PostsApprovedAdjustmentNotOriginalValue()
    {
        var sale = Sale("INV-001", null, "Cash", paid: 118m, due: 0m, (18m, 100m, 18m));
        var root = Parse(await RenderAsync(Dataset(
            [sale],
            [Return("RET-1", sale, financial: 50m, taxable: 42.37m, tax: 7.63m, nonTax: 0m, Settle(ReturnSettlementKind.ExternalRefund, 50m))])));

        var voucher = SingleVoucher(root, "Credit Note");
        Assert.Equal("RET-1", voucher.Element("VOUCHERNUMBER")!.Value);
        Assert.Equal("20260504", voucher.Element("DATE")!.Value);
        Assert.Equal("INV-001", voucher.Descendants("REFERENCENUMBER").Single().Value);
        Assert.Contains("proportional-line-tax/1", voucher.Element("NARRATION")!.Value, StringComparison.Ordinal);
        Assert.Equal(
            [("Sales", 42.37m, true), ("Output GST 18%", 7.63m, true), ("Customer Refunds Clearing", 50m, false)],
            Lines(voucher));
        AssertEveryVoucherBalances(root);
    }

    [Fact]
    public async Task RenderAsync_MixedSettlement_CreditsEachLegSeparately()
    {
        var sale = Sale("INV-001", "Asha", "Cash", paid: 78m, due: 40m, (18m, 100m, 18m));
        var root = Parse(await RenderAsync(Dataset(
            [sale],
            [
                Return("RET-1", sale, financial: 100m, taxable: 84.75m, tax: 15.25m, nonTax: 0m,
                    Settle(ReturnSettlementKind.ReceivableReduction, 40m),
                    Settle(ReturnSettlementKind.StoreCreditIssued, 60m, "CN-0001")),
            ])));

        var voucher = SingleVoucher(root, "Credit Note");
        var credits = Lines(voucher).Where(l => !l.IsDebit).ToList();
        Assert.Equal([("Asha", 40m, false), ("Credit Note Liability", 60m, false)], credits);
        Assert.Contains("CN-0001", voucher.Element("NARRATION")!.Value, StringComparison.Ordinal);
        AssertEveryVoucherBalances(root);
    }

    [Fact]
    public async Task RenderAsync_ZeroRefund_EmitsNoReturnVoucher()
    {
        var sale = Sale("INV-001", "Alice", "Cash", paid: 118m, due: 0m, (18m, 100m, 18m));

        var root = Parse(await RenderAsync(Dataset([sale], [Return("RET-0", sale, financial: 0m, taxable: 0m, tax: 0m, nonTax: 0m)])));

        Assert.DoesNotContain(root.Descendants("VOUCHER"), v => v.Element("VOUCHERTYPE")!.Value != "Sales");
    }

    [Fact]
    public async Task RenderAsync_OverRefund_DebitsCommercialAdjustmentWithoutTax()
    {
        var sale = Sale("INV-001", "Alice", "Cash", paid: 118m, due: 0m, (18m, 100m, 18m));
        var root = Parse(await RenderAsync(Dataset(
            [sale],
            [Return("RET-1", sale, financial: 130m, taxable: 100m, tax: 18m, nonTax: 12m, Settle(ReturnSettlementKind.ExternalRefund, 130m))])));

        Assert.Contains(("Sales Return Adjustments", 12m, true), Lines(SingleVoucher(root, "Credit Note")));
        AssertEveryVoucherBalances(root);
    }

    [Fact]
    public async Task RenderAsync_ReturnAcrossRates_PostsGstPerRate()
    {
        var sale = Sale("INV-001", "Alice", "Cash", paid: 223m, due: 0m, (18m, 100m, 18m), (5m, 100m, 5m));
        var entry = Return("RET-1", sale, financial: 223m, taxable: 200m, tax: 23m, nonTax: 0m, Settle(ReturnSettlementKind.ExternalRefund, 223m))
            with { TaxComponents = [new(5m, 100m, 5m), new(18m, 100m, 18m)] };

        var root = Parse(await RenderAsync(Dataset([sale], [entry])));

        Assert.Equal(
            [("Sales", 200m, true), ("Output GST 5%", 5m, true), ("Output GST 18%", 18m, true), ("Customer Refunds Clearing", 223m, false)],
            Lines(SingleVoucher(root, "Credit Note")));
    }

    [Fact]
    public async Task RenderAsync_VoidReversal_SwapsSidesAndReferencesOriginal()
    {
        var sale = Sale("INV-001", "Asha", "Cash", paid: 78m, due: 40m, (18m, 100m, 18m));
        var original = Return("RET-1", sale, financial: 100m, taxable: 84.75m, tax: 15.25m, nonTax: 0m,
            Settle(ReturnSettlementKind.ReceivableReduction, 40m),
            Settle(ReturnSettlementKind.StoreCreditIssued, 60m, "CN-0001"));
        var reversal = Negate(original) with
        {
            Kind = ReturnAdjustmentEntryKind.VoidReversal,
            PostedAt = new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero),
            VoidReason = "Entered twice",
        };

        var root = Parse(await RenderAsync(Dataset([], [reversal])));

        var voucher = SingleVoucher(root, "Journal");
        Assert.Equal("RET-1-VOID", voucher.Element("VOUCHERNUMBER")!.Value);
        Assert.Equal("20260602", voucher.Element("DATE")!.Value);
        Assert.Equal("RET-1", voucher.Descendants("REFERENCENUMBER").Single().Value);
        Assert.Equal("20260504", voucher.Descendants("REFERENCEDATE").Single().Value);
        Assert.Contains("Entered twice", voucher.Element("NARRATION")!.Value, StringComparison.Ordinal);
        Assert.Equal(
            [("Sales", 84.75m, false), ("Output GST 18%", 15.25m, false), ("Asha", 40m, true), ("Credit Note Liability", 60m, true)],
            Lines(voucher));
        AssertEveryVoucherBalances(root);
    }

    [Fact]
    public async Task RenderAsync_NeedsReview_RefusedWithDiagnostic()
    {
        var sale = Sale("INV-001", "Alice", "Cash", paid: 118m, due: 0m, (18m, 100m, 18m));
        var legacy = Return("RET-7", sale, financial: 118m, taxable: 0m, tax: 0m, nonTax: 0m) with
        {
            Status = ReturnAccountingStatus.NeedsReview,
            ReviewReason = "Payout settlement is not evidenced.",
            TaxableAdjustment = null,
            TaxAdjustment = null,
            NonTaxAdjustment = null,
            TaxComponents = [],
        };

        var result = await _renderer.RenderAsync(Dataset([sale], [legacy]), CancellationToken.None);

        Assert.True(result.IsError);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("Export.ReturnNeedsAccountingReview", error.Code);
        Assert.Contains("RET-7", error.Description, StringComparison.Ordinal);
        Assert.Contains("INV-001", error.Description, StringComparison.Ordinal);
        Assert.Contains("Payout settlement is not evidenced.", error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_SalesAndReturns_EveryVoucherBalances()
    {
        var first = Sale("INV-001", "Asha", "Cash", paid: 78m, due: 40m, (18m, 100m, 18m));
        var second = Sale("INV-002", null, "UPI", paid: 223m, due: 0m, (18m, 100m, 18m), (5m, 100m, 5m));
        var root = Parse(await RenderAsync(Dataset(
            [first, second],
            [
                Return("RET-1", first, financial: 100m, taxable: 84.75m, tax: 15.25m, nonTax: 0m,
                    Settle(ReturnSettlementKind.ReceivableReduction, 40m),
                    Settle(ReturnSettlementKind.StoreCreditIssued, 60m, "CN-0001")),
                Return("RET-2", second, financial: 50m, taxable: 42.37m, tax: 7.63m, nonTax: 0m,
                    Settle(ReturnSettlementKind.ExternalRefund, 50m)),
                Return("RET-3", second, financial: 0m, taxable: 0m, tax: 0m, nonTax: 0m),
            ])));

        Assert.Equal(2, root.Descendants("VOUCHER").Count(v => v.Element("VOUCHERTYPE")!.Value == "Sales"));
        Assert.Equal(2, root.Descendants("VOUCHER").Count(v => v.Element("VOUCHERTYPE")!.Value == "Credit Note"));
        AssertEveryVoucherBalances(root);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset)
    {
        var result = await _renderer.RenderAsync(dataset, CancellationToken.None);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        return result.Value;
    }

    /// <summary>Parses every voucher and sums its signed lines: debits must equal credits to the paisa.</summary>
    private static void AssertEveryVoucherBalances(XElement root)
    {
        foreach (var voucher in root.Descendants("VOUCHER"))
        {
            var lines = Lines(voucher);
            Assert.True(lines.Count >= 2);
            Assert.All(lines, l => Assert.True(l.Amount > 0m));
            Assert.Equal(
                lines.Where(l => l.IsDebit).Sum(l => l.Amount),
                lines.Where(l => !l.IsDebit).Sum(l => l.Amount));
        }
    }

    private static List<(string Ledger, decimal Amount, bool IsDebit)> Lines(XElement voucher) =>
        voucher.Elements("VOUCHERLINE")
            .Select(line => (
                line.Element("LEDGER")!.Value,
                decimal.Parse(line.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture),
                line.Element("ISDEBIT")!.Value == "Yes"))
            .ToList();

    private static XElement SingleVoucher(XElement root, string type) =>
        root.Descendants("VOUCHER").Single(v => v.Element("VOUCHERTYPE")!.Value == type);

    private static XElement Parse(SalesExportResult result) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(result.Content)).Root!;

    private static SalesExportDatasetDto Dataset(
        IReadOnlyList<(SalesExportSummaryRowDto Summary, List<SalesExportLineItemRowDto> Lines)> sales,
        IReadOnlyList<ReturnAdjustmentEntry>? returns = null)
    {
        var lines = sales.SelectMany(s => s.Lines).ToList();
        var taxBreakup = lines
            .GroupBy(l => l.TaxRatePercent)
            .Select(g => new SalesExportTaxBreakupDto(g.Key, g.Sum(l => l.TaxableAmount), g.Sum(l => l.TaxAmount), 0m, 0m))
            .ToList();
        var metadata = new SalesExportMetadataDto(
            "Green Mart",
            "12 Market Lane, Mumbai",
            "27ABCDE1234F1Z5",
            "Ravi Kumar",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            SalesExportLevel.LineItems);

        return new SalesExportDatasetDto(metadata, sales.Select(s => s.Summary).ToList(), lines, taxBreakup, returns ?? []);
    }

    private static (SalesExportSummaryRowDto Summary, List<SalesExportLineItemRowDto> Lines) Sale(
        string invoiceNumber,
        string? customerName,
        string paymentMethod,
        decimal paid,
        decimal due,
        params (decimal Rate, decimal Taxable, decimal Tax)[] lines)
    {
        var taxable = lines.Sum(l => l.Taxable);
        var tax = lines.Sum(l => l.Tax);
        var summary = new SalesExportSummaryRowDto(
            invoiceNumber, SoldAt, customerName, paymentMethod, paid, due, taxable + tax, 0m, taxable, tax, taxable + tax,
            null, 0m, 0m, 0m, taxable + tax, false, lines.Length);
        var lineRows = lines
            .Select(l => new SalesExportLineItemRowDto(
                invoiceNumber, SoldAt, customerName, "Product", 1m, l.Taxable, 0m, 0m, l.Rate, l.Taxable, l.Tax, l.Taxable + l.Tax, false, 0m, null, null))
            .ToList();
        return (summary, lineRows);
    }

    private static ReturnAdjustmentEntry Return(
        string returnNumber,
        (SalesExportSummaryRowDto Summary, List<SalesExportLineItemRowDto> Lines) sale,
        decimal financial,
        decimal taxable,
        decimal tax,
        decimal nonTax,
        params ReturnAdjustmentSettlement[] settlements) =>
        new(
            ReturnAdjustmentEntryKind.Return,
            ReturnedAt,
            Guid.NewGuid(),
            returnNumber,
            ReturnedAt,
            Guid.NewGuid(),
            sale.Summary.InvoiceNumber,
            sale.Summary.CustomerName,
            ReturnAccountingStatus.Balanced,
            "proportional-line-tax/1",
            null,
            OriginalReturnedGross: 118m,
            financial,
            taxable,
            tax,
            nonTax,
            taxable == 0m && tax == 0m ? [] : [new ReturnAdjustmentTaxComponent(18m, taxable, tax)],
            settlements,
            [],
            null);

    private static ReturnAdjustmentSettlement Settle(ReturnSettlementKind kind, decimal amount, string? creditNoteCode = null) =>
        new(kind, amount, null, creditNoteCode is null ? null : Guid.NewGuid(), creditNoteCode);

    private static ReturnAdjustmentEntry Negate(ReturnAdjustmentEntry entry) =>
        entry with
        {
            OriginalReturnedGross = -entry.OriginalReturnedGross,
            FinancialAdjustment = -entry.FinancialAdjustment,
            TaxableAdjustment = -entry.TaxableAdjustment,
            TaxAdjustment = -entry.TaxAdjustment,
            NonTaxAdjustment = -entry.NonTaxAdjustment,
            TaxComponents = entry.TaxComponents.Select(c => c with { TaxableAmount = -c.TaxableAmount, TaxAmount = -c.TaxAmount }).ToList(),
            Settlements = entry.Settlements.Select(s => s with { Amount = -s.Amount }).ToList(),
        };
}
