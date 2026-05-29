namespace Intelibill.Application.Features.Sales.Queries.SearchSellables;

public sealed record SearchSellablesQuery(
    Guid UserId,
    Guid ShopId,
    string SearchTerm,
    bool IsBarcodeLookup = false);
