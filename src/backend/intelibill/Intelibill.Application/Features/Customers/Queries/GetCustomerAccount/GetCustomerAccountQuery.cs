namespace Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;

/// <param name="From">First business date of the statement window; entries before it make up the opening balance.</param>
/// <param name="To">Last business date of the statement window; entries after it are left out.</param>
/// <param name="AsOf">The business date overdue is judged on; today (IST) when absent.</param>
public sealed record GetCustomerAccountQuery(
    Guid UserId,
    Guid ActiveShopId,
    Guid CustomerId,
    DateOnly? From = null,
    DateOnly? To = null,
    DateOnly? AsOf = null);
