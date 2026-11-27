namespace Intelibill.Application.Features.Items.Queries.GetItems;

public sealed record GetItemsQuery(
    Guid UserId,
    Guid ActiveShopId,
    string? Search = null,
    string? Status = null,
    int PageNumber = 1,
    int PageSize = 20);
