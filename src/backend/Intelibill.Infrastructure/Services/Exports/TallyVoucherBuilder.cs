using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Infrastructure.Services.Exports;

internal static class TallyVoucherBuilder
{
    internal static XElement BuildSalesVoucher(
        SalesExportSummaryRowDto summary,
        List<SalesExportLineItemRowDto> lineItems,
        IReadOnlyList<SalesExportTaxBreakupDto> taxBreakup,
        bool hasSingleInvoiceInDataset)
    {
        var voucher = new XElement("VOUCHER", new XAttribute("ACTION", "Create"));
        voucher.Add(new XElement("VOUCHERNUMBER", summary.InvoiceNumber));
        voucher.Add(new XElement("VOUCHERTYPE", "Sales"));
        voucher.Add(new XElement("DATE", summary.SaleDate.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));

        var referenceNumber = new XElement("REFERENCE");
        referenceNumber.Add(new XElement("REFERENCENUMBER", summary.InvoiceNumber));
        referenceNumber.Add(new XElement("REFERENCEDATE", summary.SaleDate.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
        voucher.Add(referenceNumber);

        var lineNumber = 1;
        var taxableAmount = summary.TaxableAmount;
        var salesLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
        salesLine.Add(new XElement("LEDGER", "Sales"));
        salesLine.Add(new XElement("AMOUNT", Math.Abs(taxableAmount).ToString("F2", CultureInfo.InvariantCulture)));
        salesLine.Add(new XElement("ISDEBIT", "No"));
        voucher.Add(salesLine);
        lineNumber++;

        var customerName = !string.IsNullOrWhiteSpace(summary.CustomerName) ? summary.CustomerName : "Walk-in Customer";
        if (summary.DueAmount > 0)
        {
            var customerLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
            customerLine.Add(new XElement("LEDGER", customerName));
            customerLine.Add(new XElement("AMOUNT", summary.DueAmount.ToString("F2", CultureInfo.InvariantCulture)));
            customerLine.Add(new XElement("ISDEBIT", "Yes"));
            voucher.Add(customerLine);
            lineNumber++;
        }

        if (summary.PaidAmount > 0)
        {
            var paymentLedger = GetPaymentLedgerName(summary.PaymentMethod);
            var paymentLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
            paymentLine.Add(new XElement("LEDGER", paymentLedger));
            paymentLine.Add(new XElement("AMOUNT", summary.PaidAmount.ToString("F2", CultureInfo.InvariantCulture)));
            paymentLine.Add(new XElement("ISDEBIT", "Yes"));
            voucher.Add(paymentLine);
            lineNumber++;
        }

        if (summary.CreditNoteAppliedAmount > 0)
        {
            var creditNoteLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
            creditNoteLine.Add(new XElement("LEDGER", GetCreditNoteLedgerName()));
            creditNoteLine.Add(new XElement("AMOUNT", summary.CreditNoteAppliedAmount.ToString("F2", CultureInfo.InvariantCulture)));
            creditNoteLine.Add(new XElement("ISDEBIT", "Yes"));
            voucher.Add(creditNoteLine);
            lineNumber++;
        }

        var taxEntries = BuildTaxEntries(summary, lineItems, taxBreakup, hasSingleInvoiceInDataset);
        foreach (var taxGroup in taxEntries.OrderBy(t => t.Rate))
        {
            if (taxGroup.Amount > 0)
            {
                var gstLedgerName = FormatGstLedgerName(taxGroup.Rate);
                var gstLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
                gstLine.Add(new XElement("LEDGER", gstLedgerName));
                gstLine.Add(new XElement("AMOUNT", Math.Abs(taxGroup.Amount).ToString("F2", CultureInfo.InvariantCulture)));
                gstLine.Add(new XElement("ISDEBIT", "No"));
                voucher.Add(gstLine);
                lineNumber++;
            }
        }

        return voucher;
    }

    internal static IEnumerable<(decimal Rate, decimal Amount)> BuildTaxEntries(
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
                .Select(g => (Rate: g.Key, Amount: g.Sum(i => i.TaxAmount)));
        }

        var rates = taxBreakup
            .Where(t => t.SaleTaxAmount != 0m)
            .OrderBy(t => t.TaxRatePercent)
            .ToList();

        if (rates.Count == 0)
        {
            return [];
        }

        if (hasSingleInvoiceInDataset)
        {
            return rates.Select(t => (Rate: t.TaxRatePercent, Amount: t.SaleTaxAmount));
        }

        var taxAmount = summary.TaxAmount;
        if (taxAmount == 0m)
        {
            return [];
        }

        if (rates.Count == 1)
        {
            return [(rates[0].TaxRatePercent, taxAmount)];
        }

        return [];
    }

    internal static string GetPaymentLedgerName(string paymentMethod)
    {
        return paymentMethod.ToUpperInvariant() switch
        {
            "CASH" => "Cash",
            "UPI" => "UPI",
            "CARD" => "Card",
            _ => "Customer Receivable"
        };
    }

    internal static string FormatGstLedgerName(decimal taxRate)
    {
        var rate = taxRate == (int)taxRate ? ((int)taxRate).ToString(CultureInfo.InvariantCulture) : taxRate.ToString("F2", CultureInfo.InvariantCulture);
        return $"Output GST {rate}%";
    }

    internal static string GetCreditNoteLedgerName() => "Credit Note Liability";

    internal static XElement BuildCreditNoteVoucher(SalesExportReturnRowDto returnRow)
    {
        var voucherNumber = BuildCreditNoteVoucherNumber(returnRow.CreditNoteCode, returnRow.ReturnNumber);
        var voucher = new XElement("VOUCHER", new XAttribute("ACTION", "Create"));
        voucher.Add(new XElement("VOUCHERNUMBER", voucherNumber));
        voucher.Add(new XElement("VOUCHERTYPE", "Credit Note"));
        voucher.Add(new XElement("DATE", returnRow.ProcessedAt.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));

        var referenceNumber = new XElement("REFERENCE");
        referenceNumber.Add(new XElement("REFERENCENUMBER", returnRow.InvoiceNumber));
        referenceNumber.Add(new XElement("REFERENCEDATE", returnRow.ProcessedAt.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
        voucher.Add(referenceNumber);

        var lineNumber = 1;
        var customerName = !string.IsNullOrWhiteSpace(returnRow.CustomerName) ? returnRow.CustomerName : "Walk-in Customer";
        var settlementLedger = string.IsNullOrWhiteSpace(returnRow.CreditNoteCode)
            ? customerName
            : GetCreditNoteLedgerName();
        var customerLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
        customerLine.Add(new XElement("LEDGER", settlementLedger));
        customerLine.Add(new XElement("AMOUNT", returnRow.TotalRefundAmount.ToString("F2", CultureInfo.InvariantCulture)));
        customerLine.Add(new XElement("ISDEBIT", "No"));
        voucher.Add(customerLine);
        lineNumber++;

        var salesLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
        salesLine.Add(new XElement("LEDGER", "Sales"));
        salesLine.Add(new XElement("AMOUNT", Math.Abs(returnRow.TotalTaxableAmount).ToString("F2", CultureInfo.InvariantCulture)));
        salesLine.Add(new XElement("ISDEBIT", "Yes"));
        voucher.Add(salesLine);
        lineNumber++;

        foreach (var taxEntry in returnRow.TaxBreakup.OrderBy(t => t.TaxRatePercent))
        {
            if (taxEntry.TaxAmount > 0)
            {
                var gstLedgerName = FormatGstLedgerName(taxEntry.TaxRatePercent);
                var gstLine = new XElement("VOUCHERLINE", new XAttribute("LINENUMBER", lineNumber.ToString(CultureInfo.InvariantCulture)));
                gstLine.Add(new XElement("LEDGER", gstLedgerName));
                gstLine.Add(new XElement("AMOUNT", Math.Abs(taxEntry.TaxAmount).ToString("F2", CultureInfo.InvariantCulture)));
                gstLine.Add(new XElement("ISDEBIT", "Yes"));
                voucher.Add(gstLine);
                lineNumber++;
            }
        }

        return voucher;
    }

    private static string BuildCreditNoteVoucherNumber(string? creditNoteCode, string fallbackReturnNumber)
    {
        if (string.IsNullOrWhiteSpace(creditNoteCode))
        {
            return fallbackReturnNumber;
        }

        var issuedCreditNotes = creditNoteCode
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(code => code.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct()
            .ToList();

        return issuedCreditNotes.Count > 0
            ? string.Join(", ", issuedCreditNotes)
            : fallbackReturnNumber;
    }
}
