namespace Intelibill.Application.Features.Suppliers.Queries.GetSupplier;

public sealed record GetSupplierQuery(Guid UserId, Guid ShopId, Guid SupplierId);
