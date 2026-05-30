using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Intelibill.Infrastructure.Services.Exports;

public sealed class SalesPdfExportRenderer : ISalesPdfExportRenderer
{
    private const string ContentType = "application/pdf";

    static SalesPdfExportRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pdf = new SalesPdfDocument(dataset).GeneratePdf();
        return Task.FromResult(new SalesExportResult(pdf, ContentType, "sales-export.pdf"));
    }
}
