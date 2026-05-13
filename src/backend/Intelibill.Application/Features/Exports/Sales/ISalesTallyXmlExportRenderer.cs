namespace Intelibill.Application.Features.Exports.Sales;

public interface ISalesTallyXmlExportRenderer
{
    Task<SalesExportResult> RenderAsync(SalesExportRequest request, CancellationToken cancellationToken);
}
