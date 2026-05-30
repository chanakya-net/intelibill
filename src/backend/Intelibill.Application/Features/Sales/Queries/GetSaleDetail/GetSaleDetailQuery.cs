namespace Intelibill.Application.Features.Sales.Queries.GetSaleDetail;

public sealed record GetSaleDetailQuery(Guid UserId, Guid ShopId, Guid SaleId);
