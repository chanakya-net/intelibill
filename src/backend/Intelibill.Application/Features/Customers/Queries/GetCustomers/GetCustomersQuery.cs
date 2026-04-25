namespace Intelibill.Application.Features.Customers.Queries.GetCustomers;

public sealed record GetCustomersQuery(Guid OwnerUserId, Guid ActiveShopId);
