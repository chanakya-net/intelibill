namespace Intelibill.Application.Features.Sales.Queries.GetSaleByIdempotencyKey;

public sealed record GetSaleByIdempotencyKeyQuery(
    Guid UserId,
    Guid ShopId,
    string IdempotencyKey);
