namespace Intelibill.Application.Features.Exports.Sales;

public interface ISalesPdfExportRenderer
{
    Task<SalesExportResult> RenderAsync(SalesExportRequest request, CancellationToken cancellationToken);
}
