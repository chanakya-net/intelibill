using System.Linq;
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

        return Task.FromResult(new SalesExportResult(content, "application/xml", "sales-export.xml"));
    }

    private static XElement BuildTallyEnvelope(SalesExportDatasetDto dataset)
    {
        var envelope = new XElement("ENVELOPE");

        envelope.Add(new XElement("HEADER",
            new XElement("TALLYREQUEST", "Import"),
            new XElement("TYPE", "Data"),
            new XElement("ID", "SalesVoucherExport")));

        var requestData = new XElement("REQUESTDATA");

        foreach (var ledger in TallyLedgerBuilder.ExtractUniqueLedgers(dataset))
        {
            requestData.Add(TallyLedgerBuilder.BuildTallyMessage(TallyLedgerBuilder.BuildLedgerMaster(ledger)));
        }

        if (dataset.SummaryRows.Count > 0)
        {
            var linesByInvoice = dataset.LineItemRows
                .GroupBy(r => r.InvoiceNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var summary in dataset.SummaryRows)
            {
                var voucherLines = linesByInvoice.GetValueOrDefault(summary.InvoiceNumber, new List<SalesExportLineItemRowDto>());
                requestData.Add(TallyLedgerBuilder.BuildTallyMessage(TallyVoucherBuilder.BuildSalesVoucher(
                    summary,
                    voucherLines,
                    dataset.TaxBreakup,
                    dataset.SummaryRows.Count == 1)));
            }
        }

        if (dataset.ReturnRows.Count > 0)
        {
            foreach (var returnRow in dataset.ReturnRows.Where(r => !r.IsVoided))
            {
                requestData.Add(TallyLedgerBuilder.BuildTallyMessage(TallyVoucherBuilder.BuildCreditNoteVoucher(returnRow)));
            }
        }

        var importData = new XElement("IMPORTDATA", new XElement("REQUESTDESC", "Intelibill Sales Export"), requestData);
        envelope.Add(new XElement("BODY", importData));

        return envelope;
    }
}
