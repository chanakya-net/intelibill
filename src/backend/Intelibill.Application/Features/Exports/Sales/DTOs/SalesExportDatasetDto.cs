using Intelibill.Application.Features.Exports.Accounting;

namespace Intelibill.Application.Features.Exports.Sales.DTOs;

/// <summary>
/// Sales in the period plus every return adjustment posted in it. Return figures in every format
/// come from <see cref="ReturnAdjustments"/>, including those on summary rows.
/// </summary>
public sealed record SalesExportDatasetDto(
    SalesExportMetadataDto Metadata,
    IReadOnlyList<SalesExportSummaryRowDto> SummaryRows,
    IReadOnlyList<SalesExportLineItemRowDto> LineItemRows,
    IReadOnlyList<SalesExportTaxBreakupDto> TaxBreakup,
    IReadOnlyList<ReturnAdjustmentEntry> ReturnAdjustments)
{
    public IReadOnlyList<SaleCancellationAdjustmentEntry> CancellationAdjustments { get; init; } = [];
}
