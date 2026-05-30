namespace Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;

public sealed record GetSaleByReturnNumberQuery(
    Guid UserId,
    Guid ShopId,
    string ReturnNumber);
