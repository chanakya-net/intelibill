namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record ProfitLossReportResultDto(
    IReadOnlyList<ProfitLossReportItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    ProfitLossSummaryDto Summary,
    ProfitLossAppliedFiltersDto AppliedFilters);

public sealed record ProfitLossSummaryDto(
    decimal NetProfitAfterTax,
    decimal RevenueIncludingTax,
    decimal TotalCost,
    decimal? AverageMarginPercent,
    int InvoiceCount,
    int ReturnCount,
    int AdjustmentCount);

public sealed record ProfitLossAppliedFiltersDto(
    DateOnly From,
    DateOnly To,
    string Type,
    string? Search,
    int PageNumber,
    int PageSize);
