using System.Xml.Linq;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Infrastructure.Services.Exports;

public sealed class SalesTallyXmlExportRenderer : ISalesTallyXmlExportRenderer
{
    public Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken)
    {
        var root = BuildTallyEnvelope(dataset);
        var xmlString = root.ToString(SaveOptions.None);
        var content = System.Text.Encoding.UTF8.GetBytes(xmlString);

        return Task.FromResult(new SalesExportResult(
            content,
            "application/xml",
            "sales-export.xml"));
    }

    private static XElement BuildTallyEnvelope(SalesExportDatasetDto dataset)
    {
        var envelope = new XElement("ENVELOPE");

        var header = new XElement("HEADER",
            new XElement("TALLYREQUEST", "Import"),
            new XElement("TYPE", "Data"),
            new XElement("ID", "SalesVoucherExport"));

        envelope.Add(header);

        var body = new XElement("BODY");
        var importData = new XElement("IMPORTDATA");

        var requestData = new XElement("REQUESTDATA");
        var requestDesc = new XElement("REQUESTDESC", "Intelibill Sales Export");
        importData.Add(requestDesc);

        // Build ledger master entries for unique ledgers
        var ledgers = ExtractUniqueLedgers(dataset);
        foreach (var ledger in ledgers)
        {
            requestData.Add(BuildTallyMessage(BuildLedgerMaster(ledger)));
        }

        // Build sales vouchers
        if (dataset.SummaryRows.Count > 0)
        {
            // Group line items by invoice number
            var linesByInvoice = dataset.LineItemRows
                .GroupBy(r => r.InvoiceNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Build voucher for each sale
            foreach (var summary in dataset.SummaryRows)
            {
                var voucherLines = linesByInvoice.GetValueOrDefault(summary.InvoiceNumber, new List<SalesExportLineItemRowDto>());
                var voucher = BuildSalesVoucher(
                    summary,
                    voucherLines,
                    dataset.TaxBreakup,
                    dataset.SummaryRows.Count == 1);
                requestData.Add(BuildTallyMessage(voucher));
            }
        }

        importData.Add(requestData);
        body.Add(importData);
        envelope.Add(body);

        return envelope;
    }

    private static List<string> ExtractUniqueLedgers(SalesExportDatasetDto dataset)
    {
        var ledgers = new HashSet<string>
        {
            "Sales",
            "Cash",
            "UPI",
            "Card",
            "Customer Receivable"
        };

        // Add customer names
        foreach (var row in dataset.SummaryRows)
        {
            var customerName = !string.IsNullOrWhiteSpace(row.CustomerName) ? row.CustomerName : "Walk-in Customer";
            ledgers.Add(customerName);
        }

        // Add GST output ledgers for each unique tax rate
        foreach (var taxRate in dataset.TaxBreakup.Select(t => t.TaxRatePercent).Distinct().OrderBy(r => r))
        {
            ledgers.Add(FormatGstLedgerName(taxRate));
        }

        return ledgers.OrderBy(l => l).ToList();
    }

    private static XElement BuildLedgerMaster(string ledgerName)
    {
        var master = new XElement("MASTER",
            new XAttribute("NAME", "LEDGER"),
            new XAttribute("ACTION", "Create"));

        master.Add(new XElement("NAME", ledgerName));
        master.Add(new XElement("ISDEEMEDPOSITIVE", "No"));

        // Determine ledger group based on name
        var group = GetLedgerGroup(ledgerName);
        master.Add(new XElement("GROUP", group));

        return master;
    }

    private static string GetLedgerGroup(string ledgerName)
    {
        if (ledgerName == "Sales")
            return "Sales Accounts";
        if (ledgerName == "Cash")
            return "Bank Accounts";
        if (ledgerName == "UPI" || ledgerName == "Card")
            return "Bank Accounts";
        if (ledgerName == "Customer Receivable")
            return "Sundry Debtors";
        if (ledgerName.StartsWith("Output GST", StringComparison.OrdinalIgnoreCase))
            return "Tax Payable";
        if (ledgerName == "Walk-in Customer")
            return "Sundry Debtors";

        // Default to Sundry Debtors for customer names
        return "Sundry Debtors";
    }

    private static XElement BuildTallyMessage(XElement payload)
    {
        return new XElement("TALLYMESSAGE", payload);
    }

    private static XElement BuildSalesVoucher(
        SalesExportSummaryRowDto summary,
        List<SalesExportLineItemRowDto> lineItems,
        IReadOnlyList<SalesExportTaxBreakupDto> taxBreakup,
        bool hasSingleInvoiceInDataset)
    {
        var voucher = new XElement("VOUCHER",
            new XAttribute("ACTION", "Create"));

        voucher.Add(new XElement("VOUCHERNUMBER", summary.InvoiceNumber));
        voucher.Add(new XElement("VOUCHERTYPE", "Sales"));
        voucher.Add(new XElement("DATE", summary.SaleDate.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)));

        var referenceNumber = new XElement("REFERENCE");
        referenceNumber.Add(new XElement("REFERENCENUMBER", summary.InvoiceNumber));
        referenceNumber.Add(new XElement("REFERENCEDATE", summary.SaleDate.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)));
        voucher.Add(referenceNumber);

        var lineNumber = 1;

        // Sales account line
        var netTaxableAmount = summary.TaxableAmount - summary.ReturnTaxableAmount;
        var salesLine = new XElement("VOUCHERLINE",
            new XAttribute("LINENUMBER", lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        salesLine.Add(new XElement("LEDGER", "Sales"));
        salesLine.Add(new XElement("AMOUNT", Math.Abs(netTaxableAmount).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        salesLine.Add(new XElement("ISDEBIT", "No"));
        voucher.Add(salesLine);
        lineNumber++;

        // Customer ledger line (receivable or payment)
        var customerName = !string.IsNullOrWhiteSpace(summary.CustomerName) ? summary.CustomerName : "Walk-in Customer";

        if (summary.DueAmount > 0)
        {
            // Customer owes money - debit Sundry Debtors
            var customerLine = new XElement("VOUCHERLINE",
                new XAttribute("LINENUMBER", lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            customerLine.Add(new XElement("LEDGER", customerName));
            customerLine.Add(new XElement("AMOUNT", summary.DueAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            customerLine.Add(new XElement("ISDEBIT", "Yes"));
            voucher.Add(customerLine);
            lineNumber++;
        }

        if (summary.PaidAmount > 0)
        {
            // Payment received
            var paymentLedger = GetPaymentLedgerName(summary.PaymentMethod);
            var paymentLine = new XElement("VOUCHERLINE",
                new XAttribute("LINENUMBER", lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            paymentLine.Add(new XElement("LEDGER", paymentLedger));
            paymentLine.Add(new XElement("AMOUNT", summary.PaidAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            paymentLine.Add(new XElement("ISDEBIT", "Yes"));
            voucher.Add(paymentLine);
            lineNumber++;
        }

        // Add GST lines grouped by tax rate
        var taxEntries = BuildTaxEntries(summary, lineItems, taxBreakup, hasSingleInvoiceInDataset);

        foreach (var taxGroup in taxEntries.OrderBy(t => t.Rate))
        {
            var (rate, netTax) = taxGroup;

            if (netTax > 0)
            {
                var gstLedgerName = FormatGstLedgerName(rate);
                var gstLine = new XElement("VOUCHERLINE",
                    new XAttribute("LINENUMBER", lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                gstLine.Add(new XElement("LEDGER", gstLedgerName));
                gstLine.Add(new XElement("AMOUNT", Math.Abs(netTax).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                gstLine.Add(new XElement("ISDEBIT", "No"));
                voucher.Add(gstLine);
                lineNumber++;
            }
        }

        return voucher;
    }

    private static IEnumerable<(decimal Rate, decimal Amount)> BuildTaxEntries(
        SalesExportSummaryRowDto summary,
        List<SalesExportLineItemRowDto> lineItems,
        IReadOnlyList<SalesExportTaxBreakupDto> taxBreakup,
        bool hasSingleInvoiceInDataset)
    {
        if (lineItems.Count > 0)
        {
            return lineItems
                .GroupBy(l => l.TaxRatePercent)
                .OrderBy(g => g.Key)
                .Select(g => (Rate: g.Key, Amount: g.Sum(GetNetTaxAmountForLineItem)));
        }

        var rates = taxBreakup
            .Where(t => t.NetTaxAmount != 0m)
            .OrderBy(t => t.TaxRatePercent)
            .ToList();

        if (rates.Count == 0)
        {
            return [];
        }

        if (hasSingleInvoiceInDataset)
        {
            return rates.Select(t => (Rate: t.TaxRatePercent, Amount: t.NetTaxAmount));
        }

        var netTaxAmount = summary.TaxAmount - summary.ReturnTaxAmount;
        if (netTaxAmount == 0m)
        {
            return [];
        }

        if (rates.Count == 1)
        {
            return [(rates[0].TaxRatePercent, netTaxAmount)];
        }

        return [];
    }

    private static decimal GetNetTaxAmountForLineItem(SalesExportLineItemRowDto item)
    {
        if (item.SalesQuantity <= 0)
        {
            return item.TaxAmount;
        }

        var effectiveReturnedQuantity = Math.Clamp(item.ReturnedQuantity, 0m, item.SalesQuantity);
        if (effectiveReturnedQuantity == 0m)
        {
            return item.TaxAmount;
        }

        var returnRatio = effectiveReturnedQuantity / item.SalesQuantity;
        return item.TaxAmount * (1m - returnRatio);
    }

    private static string GetPaymentLedgerName(string paymentMethod)
    {
        return paymentMethod.ToUpperInvariant() switch
        {
            "CASH" => "Cash",
            "UPI" => "UPI",
            "CARD" => "Card",
            _ => "Customer Receivable"
        };
    }

    private static string FormatGstLedgerName(decimal taxRate)
    {
        var rate = taxRate == (int)taxRate 
            ? ((int)taxRate).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : taxRate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        return $"Output GST {rate}%";
    }
}
