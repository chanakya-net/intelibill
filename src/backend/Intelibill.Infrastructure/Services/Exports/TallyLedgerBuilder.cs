using System.Linq;
using System.Xml.Linq;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Infrastructure.Services.Exports;

internal static class TallyLedgerBuilder
{
    internal static List<string> ExtractUniqueLedgers(SalesExportDatasetDto dataset)
    {
        var ledgers = new HashSet<string>
        {
            "Sales",
            "Cash",
            "UPI",
            "Card",
            "Customer Receivable",
            TallyVoucherBuilder.GetCreditNoteLedgerName()
        };

        foreach (var row in dataset.SummaryRows)
        {
            var customerName = !string.IsNullOrWhiteSpace(row.CustomerName) ? row.CustomerName : "Walk-in Customer";
            ledgers.Add(customerName);
        }

        foreach (var row in dataset.ReturnRows.Where(r => !r.IsVoided))
        {
            var customerName = !string.IsNullOrWhiteSpace(row.CustomerName) ? row.CustomerName : "Walk-in Customer";
            ledgers.Add(customerName);
        }

        foreach (var taxRate in dataset.TaxBreakup.Select(t => t.TaxRatePercent).Distinct().OrderBy(r => r))
        {
            ledgers.Add(TallyVoucherBuilder.FormatGstLedgerName(taxRate));
        }

        return ledgers.OrderBy(l => l).ToList();
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

        return "Sundry Debtors";
    }

    internal static XElement BuildTallyMessage(XElement payload)
    {
        return new XElement("TALLYMESSAGE", payload);
    }
}
