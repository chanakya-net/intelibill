using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Application.Features.Exports.Sales.Renderers;

public sealed class SalesExcelExportRenderer : ISalesExcelExportRenderer
{
    public Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SalesExportResult(Array.Empty<byte>(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "sales-export.xlsx"));
    }
}

public sealed class SalesPdfExportRenderer : ISalesPdfExportRenderer
{
    public Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SalesExportResult(Array.Empty<byte>(), "application/pdf", "sales-export.pdf"));
    }
}

public sealed class SalesTallyXmlExportRenderer : ISalesTallyXmlExportRenderer
{
    public Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SalesExportResult(Array.Empty<byte>(), "application/xml", "sales-export.xml"));
    }
}
