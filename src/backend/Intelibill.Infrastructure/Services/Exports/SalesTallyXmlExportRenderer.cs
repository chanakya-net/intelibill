using System.Xml.Linq;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Exports;

public sealed class SalesTallyXmlExportRenderer(IAccountingVoucherValidator voucherValidator) : ISalesTallyXmlExportRenderer
{
    public Task<ErrorOr<SalesExportResult>> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vouchers = BuildVouchers(dataset, cancellationToken, out var errors);
        foreach (var voucher in vouchers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = voucherValidator.Validate(voucher);
            if (validation.IsError)
                errors.AddRange(validation.Errors);
        }

        if (errors.Count > 0)
            return Task.FromResult<ErrorOr<SalesExportResult>>(errors);

        var root = BuildTallyEnvelope(dataset, vouchers, cancellationToken);
        var content = System.Text.Encoding.UTF8.GetBytes(root.ToString(SaveOptions.None));

        return Task.FromResult<ErrorOr<SalesExportResult>>(new SalesExportResult(content, "application/xml", "sales-export.xml"));
    }

    private static List<AccountingVoucher> BuildVouchers(SalesExportDatasetDto dataset, CancellationToken cancellationToken, out List<Error> errors)
    {
        errors = [];
        var vouchers = new List<AccountingVoucher>();
        var linesByInvoice = dataset.LineItemRows
            .GroupBy(r => r.InvoiceNumber)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var summary in dataset.SummaryRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var voucher = TallyVoucherBuilder.BuildSalesVoucher(
                summary,
                linesByInvoice.GetValueOrDefault(summary.InvoiceNumber, []),
                dataset.TaxBreakup,
                dataset.SummaryRows.Count == 1);

            // A zero-value invoice moves nothing in the books.
            if (voucher.Lines.Count > 0)
                vouchers.Add(voucher);
        }

        foreach (var adjustment in dataset.ReturnAdjustments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (adjustment.Status == ReturnAccountingStatus.NeedsReview)
            {
                errors.Add(Errors.Export.ReturnNeedsAccountingReview(
                    adjustment.Label,
                    adjustment.InvoiceNumber,
                    adjustment.ReviewReason ?? ReturnAdjustmentProjector.MissingSnapshotReason));
                continue;
            }

            // A zero refund keeps its stock fact in the other formats; it has nothing to post.
            if (adjustment.HasVoucher)
                vouchers.Add(TallyVoucherBuilder.BuildReturnVoucher(adjustment));
        }

        foreach (var cancellation in dataset.CancellationAdjustments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var voucher = TallyVoucherBuilder.BuildCancellationVoucher(cancellation);
            if (voucher.Lines.Count > 0) vouchers.Add(voucher);
        }

        return vouchers;
    }

    private static XElement BuildTallyEnvelope(SalesExportDatasetDto dataset, IReadOnlyList<AccountingVoucher> vouchers, CancellationToken cancellationToken)
    {
        var envelope = new XElement("ENVELOPE");

        envelope.Add(new XElement("HEADER",
            new XElement("TALLYREQUEST", "Import"),
            new XElement("TYPE", "Data"),
            new XElement("ID", "SalesVoucherExport")));

        var requestData = new XElement("REQUESTDATA");

        foreach (var ledger in TallyLedgerBuilder.ExtractUniqueLedgers(dataset, vouchers))
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestData.Add(TallyLedgerBuilder.BuildTallyMessage(TallyLedgerBuilder.BuildLedgerMaster(ledger)));
        }

        foreach (var voucher in vouchers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestData.Add(TallyLedgerBuilder.BuildTallyMessage(TallyVoucherBuilder.ToXml(voucher)));
        }

        var importData = new XElement(
            "IMPORTDATA",
            new XElement("REQUESTDESC", $"Intelibill Sales Export (accounting v{dataset.Metadata.AccountingExportVersion})"),
            requestData);
        envelope.Add(new XElement("BODY", importData));

        return envelope;
    }
}
