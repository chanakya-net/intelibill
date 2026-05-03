namespace Intelibill.Application.Features.Suppliers.Queries.GetSuppliers;

public sealed record GetSuppliersQuery(Guid UserId, Guid ActiveShopId, bool IncludeSystem = false);
