namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SalesHistoryResultDto(
    IReadOnlyList<SaleListItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    SalesHistorySummaryDto Summary);

