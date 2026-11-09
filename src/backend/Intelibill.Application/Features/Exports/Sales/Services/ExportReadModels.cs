using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales.DTOs;

namespace Intelibill.Application.Features.Exports.Sales.Services;

public sealed record ExportWindow(DateOnly From, DateOnly To);
public sealed record ExportCounts(long OutputRows, long SourceRows);
public enum ExportSource { Sales, Returns, Cancellations }
public sealed record ExportCursor(ExportSource Stream, DateTimeOffset? Timestamp = null, Guid? DocumentId = null, Guid? ChildId = null);
public sealed record ExportPage(
    IReadOnlyList<SalesExportSummaryRowDto> SummaryRows,
    IReadOnlyList<SalesExportLineItemRowDto> LineItemRows,
    IReadOnlyList<SalesExportTaxBreakupDto> TaxBreakup,
    IReadOnlyList<ReturnAdjustmentEntry> ReturnAdjustments,
    IReadOnlyList<SaleCancellationAdjustmentEntry> CancellationAdjustments,
    ExportCursor? NextCursor,
    bool IsComplete);
