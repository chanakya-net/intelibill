namespace Intelibill.Application.Features.Sales.Queries.GetSales;

public sealed record GetSalesQuery(
    Guid UserId,
    Guid ShopId,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    string? Status,
    int Page,
    int PageSize);
