using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Application.Features.Exports.Sales;

public interface ISalesPdfExportRenderer
{
    Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken);
}
