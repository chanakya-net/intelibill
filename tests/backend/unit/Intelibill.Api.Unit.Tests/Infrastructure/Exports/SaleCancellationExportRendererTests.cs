using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Services.Exports;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Exports;

public sealed class SaleCancellationExportRendererTests
{
    private static SalesExportDatasetDto Dataset() => new(new("Current shop", "Address", null, "Owner", new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero), new(2026, 11, 9), new(2026, 11, 9), SalesExportLevel.Summary), [], [],
        [new(0m, 0m, 0m, 0m, 0m) { CancellationTaxableAmount = 200m }], [])
    { CancellationAdjustments = [new(Guid.NewGuid(), Guid.NewGuid(), "SC-FROZEN", "A/2627/000001", new(2026, 11, 8), new(2026, 11, 9), new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero), "Asia/Kolkata", "Mistaken entry", "receipt-cash-due/1",
        new("Frozen seller", "Frozen shop", "Original address", "29", "Karnataka", null, TaxRegime.Unregistered), new("Frozen buyer", null, null, null, null, null), 200m, 200m, 120m, 120m, 120m, 80m, [])] };

    [Fact]
    public async Task Tally_CancellationOnlyPeriod_IsBalancedJournalWithOriginalReferencesAndFrozenBuyer()
    {
        var result = await new SalesTallyXmlExportRenderer(new AccountingVoucherValidator()).RenderAsync(Dataset(), CancellationToken.None);
        Assert.False(result.IsError);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(result.Value.Content));
        var voucher = Assert.Single(xml.Descendants("VOUCHER"));
        Assert.Equal("Journal", voucher.Element("VOUCHERTYPE")!.Value);
        Assert.Equal("SC-FROZEN", voucher.Element("VOUCHERNUMBER")!.Value);
        Assert.Equal("20261109", voucher.Element("DATE")!.Value);
        Assert.Equal("A/2627/000001", voucher.Descendants("REFERENCENUMBER").Single().Value);
        Assert.Equal("20261108", voucher.Descendants("REFERENCEDATE").Single().Value);
        var lines = voucher.Elements("VOUCHERLINE").ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, l => l.Element("LEDGER")!.Value == "Sales" && l.Element("ISDEBIT")!.Value == "Yes" && l.Element("AMOUNT")!.Value == "200.00");
        Assert.Contains(lines, l => l.Element("LEDGER")!.Value == "Cash" && l.Element("ISDEBIT")!.Value == "No" && l.Element("AMOUNT")!.Value == "80.00");
        Assert.Contains(lines, l => l.Element("LEDGER")!.Value == "Frozen buyer" && l.Element("ISDEBIT")!.Value == "No" && l.Element("AMOUNT")!.Value == "120.00");
        Assert.Contains(xml.Descendants("MASTER"), m => m.Element("NAME")!.Value == "Frozen buyer");
    }

    [Fact]
    public async Task Excel_CancellationOnlyPeriod_ShowsDedicatedReversalAndNegativeNetWithoutReturnAmount()
    {
        var result = await new SalesExcelExportRenderer().RenderAsync(Dataset(), CancellationToken.None);
        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        Assert.True(workbook.Worksheets.Contains("Sale Cancellations"));
        var sheet = workbook.Worksheet("Sales Summary");
        var total = sheet.CellsUsed().Single(c => c.GetString() == "TOTALS").Address.RowNumber;
        var header = sheet.CellsUsed().Single(c => c.GetString() == "Net Sales Amount");
        Assert.Equal(-200m, sheet.Cell(total, header.Address.ColumnNumber).GetValue<decimal>());
        var returned = sheet.CellsUsed().Single(c => c.GetString() == "Return Amount");
        Assert.Equal(0m, sheet.Cell(total, returned.Address.ColumnNumber).GetValue<decimal>());
    }

    [Fact]
    public async Task Pdf_CancellationOnlyPeriod_SubtractsDatedRevenueAndRenders()
    {
        Assert.Equal(-200m, SalesPdfDocument.SummarizeReturns(Dataset()).NetSales);
        var result = await new SalesPdfExportRenderer().RenderAsync(Dataset(), CancellationToken.None);
        Assert.True(result.Content.AsSpan(0, 5).SequenceEqual("%PDF-"u8));
    }
}
