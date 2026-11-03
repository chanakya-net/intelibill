using System.Globalization;
using System.Xml.Linq;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Exports;

/// <summary>
/// Builds the vouchers of a Tally export as balanced <see cref="AccountingVoucher"/>s first and
/// writes XML only from those, so what is validated is exactly what is written.
/// </summary>
internal static class TallyVoucherBuilder
{
    internal const string SalesLedger = "Sales";
    internal const string WalkInCustomerLedger = "Walk-in Customer";
    internal const string CustomerReceivableLedger = "Customer Receivable";
    internal const string CreditNoteLiabilityLedger = "Credit Note Liability";

    /// <summary>Refunds paid outside the books; the tender is not recorded, so it is not guessed.</summary>
    internal const string CustomerRefundsClearingLedger = "Customer Refunds Clearing";

    /// <summary>Refund above the paid share: a commercial adjustment that reverses no tax.</summary>
    internal const string SalesReturnAdjustmentsLedger = "Sales Return Adjustments";

    internal const string ReturnVoucherType = "Credit Note";
    internal const string ReversalVoucherType = "Journal";

    internal static AccountingVoucher BuildSalesVoucher(
        SalesExportSummaryRowDto summary,
        List<SalesExportLineItemRowDto> lineItems,
        IReadOnlyList<SalesExportTaxBreakupDto> taxBreakup,
        bool hasSingleInvoiceInDataset)
    {
        var lines = new List<AccountingVoucherLine>();
        AddLine(lines, SalesLedger, Math.Abs(summary.TaxableAmount), isDebit: false);
        AddLine(lines, CustomerLedgerName(summary.CustomerName), summary.DueAmount, isDebit: true);
        AddLine(lines, GetPaymentLedgerName(summary.PaymentMethod), summary.PaidAmount, isDebit: true);
        AddLine(lines, CreditNoteLiabilityLedger, summary.CreditNoteAppliedAmount, isDebit: true);

        if (summary.TaxComponents is { } components)
        {
            // An issued document carries its own component split; the voucher posts exactly that.
            foreach (var component in components)
                AddLine(lines, FormatComponentLedgerName(component.Kind, component.RatePercent), component.Amount, isDebit: false);
        }
        else
        {
            foreach (var taxGroup in BuildTaxEntries(summary, lineItems, taxBreakup, hasSingleInvoiceInDataset).OrderBy(t => t.Rate))
                AddLine(lines, FormatGstLedgerName(taxGroup.Rate), taxGroup.Amount, isDebit: false);
        }

        // Dated on the business calendar, so a sale just after midnight IST lands in its own day and fiscal year.
        var date = BusinessCalendar.DateOf(summary.SaleDate);
        return new AccountingVoucher(summary.InvoiceNumber, "Sales", date, summary.InvoiceNumber, date, null, lines);
    }

    /// <summary>
    /// A return's credit note: revenue, output tax and any commercial remainder debited, each
    /// settlement leg credited to where the money went. A void's reversal is the same voucher with
    /// every side swapped, dated the day of the void and referencing the credit note it reverses.
    /// </summary>
    internal static AccountingVoucher BuildReturnVoucher(ReturnAdjustmentEntry entry)
    {
        var adjustmentSide = !entry.IsReversal;
        var lines = new List<AccountingVoucherLine>();
        AddLine(lines, SalesLedger, Math.Abs(entry.TaxableAdjustment ?? 0m), adjustmentSide);
        if (entry.DocumentTaxComponents is { } documentComponents)
        {
            // Reverses tax into the same component ledgers the sale's document posted to.
            foreach (var component in documentComponents)
                AddLine(lines, FormatComponentLedgerName(component.Kind, component.RatePercent), Math.Abs(component.Amount), adjustmentSide);
        }
        else
        {
            foreach (var component in entry.TaxComponents.OrderBy(c => c.TaxRatePercent))
                AddLine(lines, FormatGstLedgerName(component.TaxRatePercent), Math.Abs(component.TaxAmount), adjustmentSide);
        }
        AddLine(lines, SalesReturnAdjustmentsLedger, Math.Abs(entry.NonTaxAdjustment ?? 0m), adjustmentSide);

        foreach (var settlement in entry.Settlements)
            AddLine(lines, SettlementLedgerName(settlement.Kind, entry.CustomerName), Math.Abs(settlement.Amount), !adjustmentSide);

        // Business dates, like the sales voucher, so the reference date matches the invoice's own.
        var date = BusinessCalendar.DateOf(entry.PostedAt);
        return entry.IsReversal
            ? new AccountingVoucher(
                ReversalVoucherNumber(entry.ReturnNumber),
                ReversalVoucherType,
                date,
                entry.ReturnNumber,
                BusinessCalendar.DateOf(entry.ReturnProcessedAt),
                $"Reversal of credit note {entry.ReturnNumber} for invoice {entry.InvoiceNumber}: return voided ({entry.VoidReason}).",
                lines)
            : new AccountingVoucher(
                entry.ReturnNumber,
                ReturnVoucherType,
                date,
                entry.InvoiceNumber,
                BusinessCalendar.DateOf(entry.InvoiceDate),
                ReturnNarration(entry),
                lines);
    }

    internal static string ReversalVoucherNumber(string returnNumber) => $"{returnNumber}-VOID";

    internal static XElement ToXml(AccountingVoucher voucher)
    {
        var element = new XElement("VOUCHER", new XAttribute("ACTION", "Create"));
        element.Add(new XElement("VOUCHERNUMBER", voucher.Number));
        element.Add(new XElement("VOUCHERTYPE", voucher.Type));
        element.Add(new XElement("DATE", FormatDate(voucher.Date)));

        element.Add(new XElement("REFERENCE",
            new XElement("REFERENCENUMBER", voucher.Reference),
            new XElement("REFERENCEDATE", FormatDate(voucher.ReferenceDate))));

        if (!string.IsNullOrWhiteSpace(voucher.Narration))
            element.Add(new XElement("NARRATION", voucher.Narration));

        var lineNumber = 1;
        foreach (var line in voucher.Lines)
        {
            element.Add(new XElement("VOUCHERLINE",
                new XAttribute("LINENUMBER", (lineNumber++).ToString(CultureInfo.InvariantCulture)),
                new XElement("LEDGER", line.Ledger),
                new XElement("AMOUNT", line.Amount.ToString("F2", CultureInfo.InvariantCulture)),
                new XElement("ISDEBIT", line.IsDebit ? "Yes" : "No")));
        }

        return element;
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

        // An invoice's rate split cannot be told from dataset totals; the voucher then fails the
        // balance check instead of guessing it.
        return [];
    }

    internal static string GetPaymentLedgerName(string paymentMethod)
    {
        return paymentMethod.ToUpperInvariant() switch
        {
            "CASH" => "Cash",
            "UPI" => "UPI",
            "CARD" => "Card",
            _ => CustomerReceivableLedger
        };
    }

    internal static string FormatGstLedgerName(decimal taxRate) => $"Output GST {FormatRate(taxRate)}%";

    private static readonly string[] OutputTaxLedgerPrefixes =
        ["Output GST ", .. Enum.GetValues<TaxComponentKind>().Select(kind => $"Output {kind.ToString().ToUpperInvariant()} ")];

    internal static bool IsOutputTaxLedger(string ledgerName) =>
        OutputTaxLedgerPrefixes.Any(prefix => ledgerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Component rates keep every significant digit: half of 0.25% is 0.125%, not 0.13%.</summary>
    internal static string FormatComponentLedgerName(TaxComponentKind kind, decimal ratePercent) =>
        $"Output {kind.ToString().ToUpperInvariant()} {ratePercent.ToString("0.####", CultureInfo.InvariantCulture)}%";

    private static string FormatRate(decimal taxRate) =>
        taxRate == (int)taxRate ? ((int)taxRate).ToString(CultureInfo.InvariantCulture) : taxRate.ToString("F2", CultureInfo.InvariantCulture);

    internal static string GetCreditNoteLedgerName() => CreditNoteLiabilityLedger;

    internal static string CustomerLedgerName(string? customerName) =>
        !string.IsNullOrWhiteSpace(customerName) ? customerName : WalkInCustomerLedger;

    private static string SettlementLedgerName(ReturnSettlementKind kind, string? customerName) =>
        kind switch
        {
            ReturnSettlementKind.ReceivableReduction => CustomerLedgerName(customerName),
            ReturnSettlementKind.StoreCreditIssued => CreditNoteLiabilityLedger,
            ReturnSettlementKind.ExternalRefund => CustomerRefundsClearingLedger,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown return settlement kind."),
        };

    private static string ReturnNarration(ReturnAdjustmentEntry entry)
    {
        var storeCreditCodes = entry.Settlements
            .Where(s => s.Kind == ReturnSettlementKind.StoreCreditIssued && !string.IsNullOrWhiteSpace(s.CreditNoteCode))
            .Select(s => s.CreditNoteCode!)
            .Distinct()
            .ToList();
        var storeCredit = storeCreditCodes.Count == 0 ? string.Empty : $" Store credit {string.Join(", ", storeCreditCodes)}.";
        return $"Return {entry.ReturnNumber} against invoice {entry.InvoiceNumber} ({entry.PolicyVersion}).{storeCredit}";
    }

    private static void AddLine(List<AccountingVoucherLine> lines, string ledger, decimal amount, bool isDebit)
    {
        if (amount != 0m)
            lines.Add(new AccountingVoucherLine(ledger, amount, isDebit));
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
