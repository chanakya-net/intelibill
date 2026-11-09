namespace Intelibill.Application.Features.Customers.Queries.GetCustomer;

public sealed record GetCustomerQuery(Guid ShopId, Guid CustomerId);
