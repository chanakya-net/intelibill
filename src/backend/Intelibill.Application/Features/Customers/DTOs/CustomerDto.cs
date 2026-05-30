namespace Intelibill.Application.Features.Customers.DTOs;

public sealed record CustomerDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal OutstandingDue,
    decimal CreditLimit,
    int PurchaseCount = 0,
    decimal LifetimeRevenue = 0m,
    decimal CurrentMonthRevenue = 0m);
