namespace Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;

public sealed record GetCustomerAccountQuery(
    Guid UserId,
    Guid ActiveShopId,
    Guid CustomerId);
