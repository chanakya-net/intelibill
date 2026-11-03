using System.Xml.Linq;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Infrastructure.Services.Exports;

internal static class TallyLedgerBuilder
{
    /// <summary>The standard ledgers, every GST rate in the period and every ledger a voucher posts to.</summary>
    internal static List<string> ExtractUniqueLedgers(SalesExportDatasetDto dataset, IEnumerable<AccountingVoucher> vouchers)
    {
        var ledgers = new HashSet<string>(StringComparer.Ordinal)
        {
            TallyVoucherBuilder.SalesLedger,
            "Cash",
            "UPI",
            "Card",
            TallyVoucherBuilder.CustomerReceivableLedger,
            TallyVoucherBuilder.CreditNoteLiabilityLedger,
        };

        foreach (var row in dataset.SummaryRows)
            ledgers.Add(TallyVoucherBuilder.CustomerLedgerName(row.CustomerName));

        foreach (var taxRate in dataset.TaxBreakup.Select(t => t.TaxRatePercent).Distinct())
            ledgers.Add(TallyVoucherBuilder.FormatGstLedgerName(taxRate));

        foreach (var line in vouchers.SelectMany(v => v.Lines))
            ledgers.Add(line.Ledger);

        return ledgers.Order(StringComparer.Ordinal).ToList();
    }

    internal static XElement BuildLedgerMaster(string ledgerName)
    {
        var master = new XElement("MASTER",
            new XAttribute("NAME", "LEDGER"),
            new XAttribute("ACTION", "Create"));

        master.Add(new XElement("NAME", ledgerName));
        master.Add(new XElement("ISDEEMEDPOSITIVE", "No"));
        master.Add(new XElement("GROUP", GetLedgerGroup(ledgerName)));

        return master;
    }

    internal static string GetLedgerGroup(string ledgerName)
    {
        if (ledgerName == TallyVoucherBuilder.SalesLedger)
            return "Sales Accounts";
        if (ledgerName == "Cash")
            return "Bank Accounts";
        if (ledgerName == "UPI" || ledgerName == "Card")
            return "Bank Accounts";
        if (ledgerName == TallyVoucherBuilder.CustomerReceivableLedger)
            return "Sundry Debtors";
        if (TallyVoucherBuilder.IsOutputTaxLedger(ledgerName))
            return "Tax Payable";
        if (ledgerName is TallyVoucherBuilder.CreditNoteLiabilityLedger or TallyVoucherBuilder.CustomerRefundsClearingLedger)
            return "Current Liabilities";
        if (ledgerName == TallyVoucherBuilder.SalesReturnAdjustmentsLedger)
            return "Indirect Expenses";
        if (ledgerName == TallyVoucherBuilder.WalkInCustomerLedger)
            return "Sundry Debtors";

        return "Sundry Debtors";
    }

    internal static XElement BuildTallyMessage(XElement payload)
    {
        return new XElement("TALLYMESSAGE", payload);
    }
}
