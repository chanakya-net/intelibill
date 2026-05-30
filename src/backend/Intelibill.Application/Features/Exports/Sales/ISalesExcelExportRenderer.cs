using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Application.Features.Exports.Sales;

public interface ISalesExcelExportRenderer
{
    Task<SalesExportResult> RenderAsync(SalesExportDatasetDto dataset, CancellationToken cancellationToken);
}
