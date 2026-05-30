namespace Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;

public sealed record GetProfitLossReportQuery(
    Guid UserId,
    Guid ShopId,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Type = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);
