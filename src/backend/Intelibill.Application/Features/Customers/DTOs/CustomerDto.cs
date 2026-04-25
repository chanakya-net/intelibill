namespace Intelibill.Application.Features.Customers.DTOs;

public sealed record CustomerDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal OutstandingDue);
