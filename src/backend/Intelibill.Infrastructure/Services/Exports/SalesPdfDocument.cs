using System.Globalization;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Intelibill.Infrastructure.Services.Exports;

internal sealed class SalesPdfDocument : IDocument
{
    private readonly SalesExportDatasetDto _dataset;

    public SalesPdfDocument(SalesExportDatasetDto dataset)
    {
        _dataset = dataset;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(10).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        var metadata = _dataset.Metadata;
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(metadata.ShopName).FontSize(14).SemiBold();

            if (!string.IsNullOrWhiteSpace(metadata.ShopGstin))
            {
                column.Item().Text($"GSTIN: {metadata.ShopGstin}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.ShopAddress))
            {
                column.Item().Text(metadata.ShopAddress);
            }

            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text("Sales Export").FontSize(12).SemiBold();
                row.ConstantItem(240).AlignRight().Text($"{metadata.StartDate:yyyy-MM-dd} to {metadata.EndDate:yyyy-MM-dd}");
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Generated: {metadata.GeneratedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
                row.ConstantItem(240).AlignRight().Text($"By: {metadata.GeneratedBy}");
            });

            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            column.Item().Element(ComposeSummaryMetrics);

            column.Item().Element(ComposeTable);

            if (_dataset.CancellationAdjustments.Count > 0)
                column.Item().Element(ComposeCancellations);

            if (_dataset.ReturnAdjustments.Count > 0)
            {
                column.Item().Element(ComposeReturnAdjustments);
            }
        });
    }

    private void ComposeSummaryMetrics(IContainer container)
    {
        var summaryRows = _dataset.SummaryRows;

        var totalInvoices = summaryRows.Count;
        var grossSales = summaryRows.Sum(r => r.TotalBeforeDiscount);
        var totalDiscounts = summaryRows.Sum(r => r.DiscountAmount);
        var taxableAmount = summaryRows.Sum(r => r.TaxableAmount);
        var taxAmount = summaryRows.Sum(r => r.TaxAmount);
        var returnTotals = SummarizeReturns(_dataset);
        var returnAmount = returnTotals.Adjustment;
        var netSales = returnTotals.NetSales;
        var paidAmount = summaryRows.Sum(r => r.PaidAmount);
        var dueAmount = summaryRows.Sum(r => r.DueAmount);
        var creditNoteAppliedAmount = summaryRows.Sum(r => r.CreditNoteAppliedAmount);
        var creditNoteIssuedAmount = returnTotals.StoreCreditIssued;
        var reviewCount = returnTotals.NeedingReview;

        container.Column(column =>
        {
            column.Item().Text("Summary").SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.ConstantColumn(110);
                    cols.RelativeColumn();
                    cols.ConstantColumn(110);
                });

                static IContainer LabelStyle(IContainer c) => c.PaddingVertical(2).PaddingRight(6);
                static IContainer ValueStyle(IContainer c) => c.PaddingVertical(2).AlignRight();

                void LabelCell(string text) =>
                    table.Cell().Element(LabelStyle).Text(text).FontColor(Colors.Grey.Darken2);

                void ValueCell(string text) =>
                    table.Cell().Element(ValueStyle).Text(text);

                var culture = CultureInfo.InvariantCulture;
                LabelCell("Total invoices");
                ValueCell(totalInvoices.ToString("N0", culture));
                LabelCell("Gross sales");
                ValueCell(grossSales.ToString("N2", culture));

                LabelCell("Discounts");
                ValueCell(totalDiscounts.ToString("N2", culture));
                LabelCell("Taxable amount");
                ValueCell(taxableAmount.ToString("N2", culture));

                LabelCell("Tax amount");
                ValueCell(taxAmount.ToString("N2", culture));
                LabelCell("Return adjustments");
                ValueCell(returnAmount.ToString("N2", culture));

                LabelCell("Cancellation count");
                ValueCell(_dataset.CancellationAdjustments.Count.ToString("N0", culture));
                LabelCell("Cancellation amount");
                ValueCell(_dataset.CancellationAdjustments.Sum(c => c.TotalAmount).ToString("N2", culture));

                LabelCell("Net sales");
                ValueCell(netSales.ToString("N2", culture));
                LabelCell("Paid amount");
                ValueCell(paidAmount.ToString("N2", culture));

                LabelCell("Credit note applied");
                ValueCell(creditNoteAppliedAmount.ToString("N2", culture));

                LabelCell("Credit notes issued");
                ValueCell(creditNoteIssuedAmount.ToString("N2", culture));

                LabelCell("Due amount");
                ValueCell(dueAmount.ToString("N2", culture));
                LabelCell("Returns needing review");
                ValueCell(reviewCount.ToString("N0", culture));
            });
        });
    }

    private void ComposeTable(IContainer container)
    {
        var isLineItem = string.Equals(_dataset.Metadata.ExportLevel, SalesExportLevel.LineItems, StringComparison.OrdinalIgnoreCase);

        container.Column(column =>
        {
            column.Item().Text(isLineItem ? "Line items" : "Invoices").SemiBold();
            column.Item().Table(table =>
            {
                if (isLineItem)
                {
                    ComposeLineItemTable(table);
                }
                else
                {
                    ComposeSummaryTable(table);
                }
            });
        });
    }

    /// <summary>
    /// Return figures cover every adjustment posted in the period, including returns and voids of
    /// sales made before it, so they agree with the Tally and spreadsheet exports.
    /// </summary>
    internal static SalesPdfReturnTotals SummarizeReturns(SalesExportDatasetDto dataset)
    {
        var adjustments = dataset.ReturnAdjustments;
        var adjustment = adjustments.Sum(a => a.FinancialAdjustment);
        return new SalesPdfReturnTotals(
            adjustment,
            adjustments.Sum(a => a.SettledBy(ReturnSettlementKind.StoreCreditIssued)),
            dataset.SummaryRows.Sum(r => r.TotalAmount) - adjustment - dataset.CancellationAdjustments.Sum(c => c.TotalAmount),
            adjustments.Count(a => a.Status == ReturnAccountingStatus.NeedsReview));
    }

    private void ComposeCancellations(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Sale Cancellations").SemiBold();
            foreach (var entry in _dataset.CancellationAdjustments)
            {
                column.Item().Text($"{entry.ReferenceNumber} — {entry.BusinessDate:yyyy-MM-dd}").SemiBold();
                column.Item().Text($"Original invoice: {entry.InvoiceNumber}, {entry.InvoiceBusinessDate:yyyy-MM-dd}. Seller: {entry.Seller.LegalName}. Buyer: {entry.CustomerName ?? "Walk-in Customer"}.");
                column.Item().Text($"Reason: {entry.Reason}");
                column.Item().Text(FormattableString.Invariant($"Revenue reversal: {-entry.RevenueAmount:N2}; COGS reversal: {-entry.RecordedCogs:N2}; due reduction: {entry.DueReductionAmount:N2}; operator-confirmed cash refund: {entry.CashRefundAmount:N2}."));
                foreach (var line in entry.Lines)
                    column.Item().Text(FormattableString.Invariant($"Recovered: {line.Description} — {line.Quantity:0.###} {line.Uqc}; recorded cost: {line.RecordedCostAmount:N2}."));
                column.Item().Text(entry.Disclaimer).Italic();
            }
        });
    }

    private void ComposeReturnAdjustments(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("Return Adjustments").SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(50); // Date
                    cols.RelativeColumn();   // Return
                    cols.ConstantColumn(62); // Invoice
                    cols.ConstantColumn(52); // Original value
                    cols.ConstantColumn(52); // Adjustment
                    cols.ConstantColumn(48); // Tax
                    cols.ConstantColumn(52); // Receivable
                    cols.ConstantColumn(52); // Store credit
                    cols.ConstantColumn(52); // Refund
                    cols.ConstantColumn(50); // Status
                });

                ComposeTableHeader(
                    table,
                    ["Date", "Return", "Invoice", "Original", "Adjustment", "Tax", "Receivable", "Store credit", "Refund", "Status"]);

                var rowIndex = 0;
                foreach (var entry in _dataset.ReturnAdjustments)
                {
                    rowIndex++;
                    var background = rowIndex % 2 == 0 ? Colors.Grey.Lighten5 : Colors.White;

                    table.Cell().Element(c => TableCell(c, background)).Text(entry.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    table.Cell().Element(c => TableCell(c, background)).Text(entry.Label);
                    table.Cell().Element(c => TableCell(c, background)).Text(entry.InvoiceNumber);
                    AmountCell(table, background, entry.OriginalReturnedGross);
                    AmountCell(table, background, entry.FinancialAdjustment);
                    AmountCell(table, background, entry.TaxAdjustment ?? 0m);
                    AmountCell(table, background, entry.SettledBy(ReturnSettlementKind.ReceivableReduction));
                    AmountCell(table, background, entry.SettledBy(ReturnSettlementKind.StoreCreditIssued));
                    AmountCell(table, background, entry.SettledBy(ReturnSettlementKind.ExternalRefund));
                    table.Cell().Element(c => TableCell(c, background)).Text(StatusLabel(entry));
                }
            });
        });
    }

    private static void AmountCell(TableDescriptor table, string background, decimal amount) =>
        table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(amount.ToString("N2", CultureInfo.InvariantCulture));

    internal static string StatusLabel(ReturnAdjustmentEntry entry) =>
        entry.Status == ReturnAccountingStatus.NeedsReview ? "Needs review" : "Balanced";

    private void ComposeSummaryTable(TableDescriptor table)
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(70); // Invoice
            cols.ConstantColumn(58); // Date
            cols.RelativeColumn();   // Customer
            cols.ConstantColumn(52); // Paid
            cols.ConstantColumn(52); // Due
            cols.ConstantColumn(70); // Net
            cols.ConstantColumn(74); // Credit Note Applied
            cols.ConstantColumn(74); // Credit Note Issued
            cols.ConstantColumn(80); // Credit Note Issued Amount
        });

        ComposeTableHeader(
            table,
            ["Invoice", "Date", "Customer", "Paid", "Due", "Net", "CN Applied", "CN Issued", "CN Issued Amount"]);

        var rowIndex = 0;
        foreach (var row in _dataset.SummaryRows)
        {
            rowIndex++;
            var zebra = rowIndex % 2 == 0;
            var background = zebra ? Colors.Grey.Lighten5 : Colors.White;

            table.Cell().Element(c => TableCell(c, background)).Text(row.InvoiceNumber + (row.Status == "Cancelled" ? $" — Cancelled ({row.CancellationReference}, {row.CancellationBusinessDate:yyyy-MM-dd})" : string.Empty));
            table.Cell().Element(c => TableCell(c, background)).Text(row.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background)).Text(row.CustomerName ?? string.Empty);
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.PaidAmount.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.DueAmount.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.NetSalesAmount.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight())
                .Text(row.CreditNoteAppliedAmount.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background)).Text(row.IssuedCreditNoteCodes ?? string.Empty);
            table.Cell().Element(c => TableCell(c, background).AlignRight())
                .Text(row.IssuedCreditNoteAmount.ToString("N2", CultureInfo.InvariantCulture));
        }
    }

    private void ComposeLineItemTable(TableDescriptor table)
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(62); // Invoice
            cols.RelativeColumn();   // Item
            cols.ConstantColumn(36); // Qty
            cols.ConstantColumn(44); // Rate
            cols.ConstantColumn(36); // GST
            cols.ConstantColumn(52); // Taxable
            cols.ConstantColumn(46); // Tax
            cols.ConstantColumn(56); // Total
        });

        ComposeTableHeader(table, ["Invoice", "Item", "Qty", "Rate", "GST%", "Taxable", "Tax", "Total"]);

        var rowIndex = 0;
        foreach (var row in _dataset.LineItemRows)
        {
            rowIndex++;
            var zebra = rowIndex % 2 == 0;
            var background = zebra ? Colors.Grey.Lighten5 : Colors.White;

            table.Cell().Element(c => TableCell(c, background)).Text(row.InvoiceNumber + (row.Status == "Cancelled" ? $" — Cancelled ({row.CancellationReference}, {row.CancellationBusinessDate:yyyy-MM-dd})" : string.Empty));
            table.Cell().Element(c => TableCell(c, background)).Text(row.ItemName);
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.SalesQuantity.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.SalesPrice.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.TaxRatePercent.ToString("N0", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.TaxableAmount.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.TaxAmount.ToString("N2", CultureInfo.InvariantCulture));
            table.Cell().Element(c => TableCell(c, background).AlignRight()).Text(row.LineTotal.ToString("N2", CultureInfo.InvariantCulture));
        }
    }

    private static void ComposeTableHeader(TableDescriptor table, IReadOnlyList<string> headers)
    {
        table.Header(header =>
        {
            foreach (var text in headers)
            {
                header.Cell()
                    .Background(Colors.Grey.Lighten3)
                    .PaddingVertical(4)
                    .PaddingHorizontal(3)
                    .Text(text)
                    .SemiBold()
                    .FontSize(8);
            }
        });
    }

    private static IContainer TableCell(IContainer container, string background) =>
        container
            .Background(background)
            .PaddingVertical(3)
            .PaddingHorizontal(3)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2)
            .DefaultTextStyle(x => x.FontSize(8));

    private void ComposeFooter(IContainer container)
    {
        var generatedBy = _dataset.Metadata.GeneratedBy;
        container.Column(column =>
        {
            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text("Generated by Intelibill").FontSize(8).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignCenter()
                    .Text(string.IsNullOrWhiteSpace(generatedBy) ? string.Empty : generatedBy)
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        });
    }
}

internal sealed record SalesPdfReturnTotals(decimal Adjustment, decimal StoreCreditIssued, decimal NetSales, int NeedingReview);
