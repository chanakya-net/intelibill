namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportDatasetDto(
    SalesExportMetadataDto Metadata,
    IReadOnlyList<SalesExportSummaryRowDto> SummaryRows,
    IReadOnlyList<SalesExportLineItemRowDto> LineItemRows,
    IReadOnlyList<SalesExportTaxBreakupDto> TaxBreakup);
