namespace Intelibill.Application.Features.Exports.Sales;

public interface ISalesExcelExportRenderer
{
    Task<SalesExportResult> RenderAsync(SalesExportRequest request, CancellationToken cancellationToken);
}
