using Intelibill.Application.Features.Exports.ProfitLoss.DTOs;

namespace Intelibill.Application.Features.Exports.ProfitLoss;

public interface IProfitLossExcelExportRenderer
{
    Task<ProfitLossExportResult> RenderAsync(ProfitLossExportDatasetDto dataset, CancellationToken cancellationToken);
}
