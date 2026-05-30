namespace Intelibill.Application.Features.Exports.ProfitLoss.DTOs;

public sealed record ProfitLossExportDatasetDto(
    ProfitLossExportMetadataDto Metadata,
    ProfitLossExportSummaryDto Summary,
    IReadOnlyList<ProfitLossExportRowDto> Rows);
